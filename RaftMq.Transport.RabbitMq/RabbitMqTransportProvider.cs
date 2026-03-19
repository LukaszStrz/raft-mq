using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;

namespace RaftMq.Transport.RabbitMq;

public class RabbitMqTransportProvider<T> : ITransport<T>, IDisposable where T : IRaftCommand
{
    private readonly string _nodeId;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqTransportProvider<T>> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private IConnection? _connection;
    private IModel? _channel;
    
    // For listening to incoming RPCs
    private readonly string _queueName;
    private const string ExchangeName = "raft.exchange";
    private const int MaxConnectionRetries = 5;
    private const int ConnectionRetryDelayMs = 2000;

    // For sending RPCs and waiting for replies
    private readonly string _replyQueueName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new();

    public event EventHandler<string>? OnNodeDiscovered;

    private Func<AppendEntriesRequest<T>, Task<AppendEntriesResponse>>? _appendEntriesHandler;
    private Func<RequestVoteRequest, Task<RequestVoteResponse>>? _requestVoteHandler;

    public RabbitMqTransportProvider(
        string nodeId,
        IConnectionFactory connectionFactory,
        ILogger<RabbitMqTransportProvider<T>> logger,
        JsonSerializerOptions? jsonOptions = null)
    {
        _nodeId = nodeId;
        _queueName = $"raft.node.{_nodeId}";
        _replyQueueName = $"raft.reply.{_nodeId}.{Guid.NewGuid():N}";

        // Ensure async consumers are enabled is the caller's responsibility, but we assume it here
        _connectionFactory = connectionFactory;
        _logger = logger;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        int retries = MaxConnectionRetries;
        while (retries-- > 0)
        {
            try
            {
                _connection = _connectionFactory.CreateConnection();
                _channel = _connection.CreateModel();
                break;
            }
            catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
            {
                if (retries == 0)
                {
                    _logger.LogCritical(ex, "Node {NodeId} could not connect to RabbitMQ.", _nodeId);
                    throw;
                }
                
                _logger.LogWarning("Node {NodeId} failed to connect. Retrying in {DelayMs}ms...", _nodeId, ConnectionRetryDelayMs);
                await Task.Delay(ConnectionRetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        _channel!.ExchangeDeclare(ExchangeName, ExchangeType.Direct);

        _channel.ExchangeDeclare("raft.discovery", ExchangeType.Fanout);
        var dq = _channel.QueueDeclare().QueueName;
        _channel.QueueBind(dq, "raft.discovery", routingKey: "");
        
        var dc = new AsyncEventingBasicConsumer(_channel);
        dc.Received += (sender, ea) =>
        {
            var discoveredNodeId = Encoding.UTF8.GetString(ea.Body.ToArray());
            if (discoveredNodeId != _nodeId)
            {
                OnNodeDiscovered?.Invoke(this, discoveredNodeId);
            }
            return Task.CompletedTask;
        };
        _channel.BasicConsume(dq, autoAck: true, dc);

        var bodyDiscovery = Encoding.UTF8.GetBytes(_nodeId);
        _channel.BasicPublish(exchange: "raft.discovery", routingKey: "", basicProperties: null, body: bodyDiscovery);

        // 1. Setup incoming RPC queue
        _channel.QueueDeclare(_queueName, durable: false, exclusive: false, autoDelete: true);
        _channel.QueueBind(_queueName, ExchangeName, routingKey: _nodeId);

        var rpcConsumer = new AsyncEventingBasicConsumer(_channel);
        rpcConsumer.Received += OnRpcRequestReceivedAsync;
        _channel.BasicConsume(_queueName, autoAck: false, rpcConsumer);

        // 2. Setup reply queue for outgoing RPCs
        _channel.QueueDeclare(_replyQueueName, durable: false, exclusive: true, autoDelete: true);
        var replyConsumer = new AsyncEventingBasicConsumer(_channel);
        replyConsumer.Received += OnRpcReplyReceivedAsync;
        _channel.BasicConsume(_replyQueueName, autoAck: true, replyConsumer);

        _logger.LogInformation("Node {NodeId} RabbitMQ transport started.", _nodeId);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _channel?.Dispose();
        _connection?.Dispose();
        return Task.CompletedTask;
    }

    public void RegisterAppendEntriesHandler(Func<AppendEntriesRequest<T>, Task<AppendEntriesResponse>> handler)
    {
        _appendEntriesHandler = handler;
    }

    public void RegisterRequestVoteHandler(Func<RequestVoteRequest, Task<RequestVoteResponse>> handler)
    {
        _requestVoteHandler = handler;
    }

    public async Task<AppendEntriesResponse> SendAppendEntriesAsync(string targetNodeId, AppendEntriesRequest<T> request, CancellationToken cancellationToken = default)
    {
        var jsonResponse = await SendRpcAsync(targetNodeId, "AppendEntries", request, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AppendEntriesResponse>(jsonResponse, _jsonOptions)!;
    }

    public async Task<RequestVoteResponse> SendRequestVoteAsync(string targetNodeId, RequestVoteRequest request, CancellationToken cancellationToken = default)
    {
        var jsonResponse = await SendRpcAsync(targetNodeId, "RequestVote", request, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RequestVoteResponse>(jsonResponse, _jsonOptions)!;
    }

    private async Task<string> SendRpcAsync<TMsg>(string targetNodeId, string rpcType, TMsg message, CancellationToken cancellationToken)
    {
        if (_channel == null) throw new InvalidOperationException("Transport is not started.");

        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[correlationId] = tcs;

        var props = _channel.CreateBasicProperties();
        props.CorrelationId = correlationId;
        props.ReplyTo = _replyQueueName;
        props.Type = rpcType;

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _jsonOptions));

        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: targetNodeId,
            basicProperties: props,
            body: body);

        using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }

    private async Task OnRpcRequestReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        string? responseJson = null;
        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            
            if (ea.BasicProperties.Type == "AppendEntries" && _appendEntriesHandler != null)
            {
                var req = JsonSerializer.Deserialize<AppendEntriesRequest<T>>(body, _jsonOptions)!;
                var res = await _appendEntriesHandler(req).ConfigureAwait(false);
                responseJson = JsonSerializer.Serialize(res, _jsonOptions);
            }
            else if (ea.BasicProperties.Type == "RequestVote" && _requestVoteHandler != null)
            {
                var req = JsonSerializer.Deserialize<RequestVoteRequest>(body, _jsonOptions)!;
                var res = await _requestVoteHandler(req).ConfigureAwait(false);
                responseJson = JsonSerializer.Serialize(res, _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC request.");
        }

        if (responseJson != null && ea.BasicProperties.ReplyTo != null)
        {
            var replyProps = _channel!.CreateBasicProperties();
            replyProps.CorrelationId = ea.BasicProperties.CorrelationId;
            var replyBody = Encoding.UTF8.GetBytes(responseJson);

            _channel.BasicPublish(
                exchange: "",
                routingKey: ea.BasicProperties.ReplyTo,
                basicProperties: replyProps,
                body: replyBody);
        }

        _channel!.BasicAck(ea.DeliveryTag, multiple: false);
    }

    private Task OnRpcReplyReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var correlationId = ea.BasicProperties.CorrelationId;
        if (correlationId != null && _pendingRequests.TryGetValue(correlationId, out var tcs))
        {
            var response = Encoding.UTF8.GetString(ea.Body.ToArray());
            tcs.TrySetResult(response);
        }
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
