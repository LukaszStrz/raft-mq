using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RaftMq.Core;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Serialization;
using RaftMq.Demo.Console.Domain;
using RaftMq.Infrastructure.FilePersistence;
using RaftMq.Transport.RabbitMq;

namespace RaftMq.Demo.Console;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("======================================");
            System.Console.WriteLine("    Raft-MQ Demonstrator Console");
            System.Console.WriteLine("======================================");
            System.Console.WriteLine("Usage: dotnet run <NodeId> [ClusterNodes]");
            System.Console.WriteLine("Example: dotnet run nodeA nodeA,nodeB,nodeC,nodeD,nodeE");
            System.Console.WriteLine("");
            System.Console.WriteLine("If ClusterNodes is omitted, it defaults to: nodeA,nodeB,nodeC,nodeD,nodeE");
            return;
        }

        string nodeId = args[0];
        string[] clusterNodes = args.Length > 1 
            ? args[1].Split(',', StringSplitOptions.RemoveEmptyEntries) 
            : new[] { "nodeA", "nodeB", "nodeC" };
        
        var host = DefaultBuilder(args, nodeId, clusterNodes).Build();
        
        System.Console.WriteLine($"======================================");
        System.Console.WriteLine($"Starting Raft Node: {nodeId}");
        System.Console.WriteLine($"======================================");
        System.Console.WriteLine("Press Ctrl+C to shut down gracefully.");
        System.Console.WriteLine("Type 'status' to review node state.");
        System.Console.WriteLine("Type 'set <key> <value>' anywhere. (Only applies if Leader)");
        
        var runTask = host.RunAsync();
        
        var raftNode = host.Services.GetRequiredService<RaftNode<IRaftCommand>>();
        var stateMachine = host.Services.GetRequiredService<KeyValueStateMachine>();
        
        while (true)
        {
            var line = await System.Console.In.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            if (line.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                System.Console.WriteLine($"[STATUS] Node: {raftNode.NodeId} | State: {raftNode.State} | Term: {raftNode.CurrentTerm}");
            }
            else if (line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    if (raftNode.State != RaftMq.Core.Models.RaftNodeState.Leader)
                    {
                        System.Console.WriteLine("ERROR: Only the Leader can accept commands. Submit to the designated leader.");
                        continue;
                    }
                    
                    var cmd = new KeyValueCommand { Key = parts[1], Value = parts[2] };
                    System.Console.WriteLine($"[LEADER] Dispatching Command: {cmd.Key}={cmd.Value}");
                    await raftNode.ApplyCommandAsync(cmd);
                }
                else
                {
                    System.Console.WriteLine("Invalid args. Usage: set <key> <value>");
                }
            }
            else
            {
                System.Console.WriteLine("Unknown command. Try: 'set <key> <value>', 'status'");
            }
        }
    }

    private static IHostBuilder DefaultBuilder(string[] args, string nodeId, string[] clusterNodes) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                var storageDir = Path.Combine(Path.GetTempPath(), "RaftMq", nodeId);
                Directory.CreateDirectory(storageDir);

                var jsonOptions = new JsonSerializerOptions();
                jsonOptions.Converters.Add(new RaftCommandJsonConverter());

                services.AddSingleton(jsonOptions);
                services.AddSingleton<KeyValueStateMachine>();
                services.AddSingleton<IStateMachine>(sp => sp.GetRequiredService<KeyValueStateMachine>());
                
                services.AddSingleton<IPersistenceProvider<IRaftCommand>>(sp => 
                    new FilePersistenceProvider<IRaftCommand>(
                        Path.Combine(storageDir, "state.json"),
                        Path.Combine(storageDir, "log.json"),
                        jsonOptions));

                services.AddSingleton<IConnectionFactory>(new ConnectionFactory 
                { 
                    Uri = new Uri("amqp://guest:guest@localhost:5672/"),
                    DispatchConsumersAsync = true 
                });

                services.AddSingleton<ITransport<IRaftCommand>>(sp =>
                    new RabbitMqTransportProvider<IRaftCommand>(
                        nodeId,
                        sp.GetRequiredService<IConnectionFactory>(),
                        sp.GetRequiredService<ILogger<RabbitMqTransportProvider<IRaftCommand>>>(),
                        jsonOptions));

                services.AddSingleton(sp => 
                    new RaftNode<IRaftCommand>(
                        nodeId,
                        clusterNodes,
                        sp.GetRequiredService<IPersistenceProvider<IRaftCommand>>(),
                        sp.GetRequiredService<IStateMachine>(),
                        sp.GetRequiredService<ITransport<IRaftCommand>>(),
                        sp.GetRequiredService<ILogger<RaftNode<IRaftCommand>>>()));

                services.AddHostedService<RaftNodeHostedService>();
            });
}

public class RaftNodeHostedService : IHostedService
{
    private readonly RaftNode<IRaftCommand> _node;
    private readonly CancellationTokenSource _cts = new();

    public RaftNodeHostedService(RaftNode<IRaftCommand> node)
    {
        _node = node;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _node.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _node.Shutdown();
        return Task.CompletedTask;
    }
}
