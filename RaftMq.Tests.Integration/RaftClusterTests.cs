using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RaftMq.Core;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;
using RaftMq.Infrastructure.FilePersistence;
using RaftMq.Transport.RabbitMq;
using Testcontainers.RabbitMq;
using Xunit;

using FluentAssertions;

namespace RaftMq.Tests.Integration;

public class DummyCommand : IRaftCommand
{
    public string Data { get; set; } = string.Empty;
}

public class DummyStateMachine : IStateMachine
{
    public List<IRaftCommand> AppliedCommands { get; } = new();

    public Task ApplyAsync(IRaftCommand command, CancellationToken cancellationToken = default)
    {
        AppliedCommands.Add(command);
        return Task.CompletedTask;
    }
}

public class RaftClusterTests : IAsyncLifetime
{
    private RabbitMqContainer _rabbitMqContainer = null!;
    private readonly List<string> _tempDirs = new();

    public async Task InitializeAsync()
    {
        _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management")
            .Build();

        await _rabbitMqContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _rabbitMqContainer.DisposeAsync();
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Cluster_Should_Elect_Leader_And_Replicate_Command()
    {
        // 1. Setup Environment
        var clusterNodes = new[] { "nodeA", "nodeB", "nodeC" };
        var nodes = new List<RaftNode<DummyCommand>>();

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqContainer.GetConnectionString()),
            DispatchConsumersAsync = true
        };

        foreach (var nodeId in clusterNodes)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"raft-mq-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);

            var persistence = new FilePersistenceProvider<DummyCommand>(
                Path.Combine(tempDir, "state.json"),
                Path.Combine(tempDir, "log.json"));

            var stateMachine = new DummyStateMachine();

            var transport = new RabbitMqTransportProvider<DummyCommand>(
                nodeId, 
                factory, 
                NullLogger<RabbitMqTransportProvider<DummyCommand>>.Instance);

            var node = new RaftNode<DummyCommand>(
                nodeId,
                clusterNodes,
                persistence,
                stateMachine,
                transport,
                NullLogger<RaftNode<DummyCommand>>.Instance);

            nodes.Add(node);
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        
        foreach (var node in nodes)
        {
            await node.InitializeAsync(cts.Token);
        }

        // Wait for election (max 10 seconds)
        RaftNode<DummyCommand>? leader = null;
        for (int i = 0; i < 100; i++)
        {
            leader = nodes.FirstOrDefault(n => n.State == RaftNodeState.Leader);
            if (leader != null) break;
            await Task.Delay(100);
        }

        leader.Should().NotBeNull();

        // Replicate command
        var cmd = new DummyCommand { Data = "Test1" };
        await leader.ApplyCommandAsync(cmd, cts.Token);

        await Task.Delay(1000); // Wait for heartbeat

        // Cleanup
        foreach (var node in nodes)
        {
            node.Shutdown();
            node.Dispose();
        }
    }
}
