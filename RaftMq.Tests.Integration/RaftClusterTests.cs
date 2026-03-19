using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RaftMq.Core;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;
using RaftMq.Core.Serialization;
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
        _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management").Build();
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

    private RaftNode<IRaftCommand> CreateNode(string nodeId, string[] clusterNodes, IConnectionFactory factory, DummyStateMachine stateMachine)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"raft-mq-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        var jsonOptions = new JsonSerializerOptions();
        jsonOptions.Converters.Add(new RaftCommandJsonConverter());

        var persistence = new FilePersistenceProvider<IRaftCommand>(
            Path.Combine(tempDir, "state.json"),
            Path.Combine(tempDir, "log.json"),
            jsonOptions);

        var transport = new RabbitMqTransportProvider<IRaftCommand>(
            nodeId, 
            factory, 
            NullLogger<RabbitMqTransportProvider<IRaftCommand>>.Instance,
            jsonOptions);

        return new RaftNode<IRaftCommand>(
            nodeId,
            clusterNodes,
            persistence,
            stateMachine,
            transport,
            NullLogger<RaftNode<IRaftCommand>>.Instance);
    }

    [Fact]
    public async Task Cluster_Should_Elect_Leader_And_Replicate_Command()
    {
        var seedNodes = new[] { "nodeA", "nodeB", "nodeC" };
        var nodes = new List<RaftNode<IRaftCommand>>();
        var stateMachines = new List<DummyStateMachine>();

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqContainer.GetConnectionString()),
            DispatchConsumersAsync = true
        };

        foreach (var nodeId in seedNodes)
        {
            var sm = new DummyStateMachine();
            var node = CreateNode(nodeId, seedNodes, factory, sm);
            nodes.Add(node);
            stateMachines.Add(sm);
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var node in nodes) await node.InitializeAsync(cts.Token);

        RaftNode<IRaftCommand>? leader = null;
        for (int i = 0; i < 150; i++)
        {
            leader = nodes.FirstOrDefault(n => n.State == RaftNodeState.Leader);
            if (leader != null) break;
            await Task.Delay(100);
        }

        leader.Should().NotBeNull();

        var cmd = new DummyCommand { Data = "Test1" };
        await leader!.ApplyCommandAsync(cmd, cts.Token);

        await Task.Delay(1500); 

        foreach (var sm in stateMachines)
        {
            sm.AppliedCommands.Should().Contain(c => c is DummyCommand && ((DummyCommand)c).Data == "Test1");
        }

        // --- NEW Dynamic Node Join Test ---
        var nodeD_sm = new DummyStateMachine();
        var nodeD = CreateNode("nodeD", seedNodes, factory, nodeD_sm);
        
        await nodeD.InitializeAsync(cts.Token);
        nodes.Add(nodeD);
        stateMachines.Add(nodeD_sm);

        // Wait for discovery to broadcast, leader to log it, and state machines to apply it.
        await Task.Delay(2500);

        var cmd2 = new DummyCommand { Data = "TestDynamic" };
        
        // Ensure leader is still active or re-elected
        leader = nodes.FirstOrDefault(n => n.State == RaftNodeState.Leader);
        leader.Should().NotBeNull("Cluster must hold election gracefully with 4th node inserted");

        await leader!.ApplyCommandAsync(cmd2, cts.Token);
        await Task.Delay(1500);

        nodeD_sm.AppliedCommands.Should().Contain(c => c is AddNodeCommand && ((AddNodeCommand)c).NodeId == "nodeD", "Node D must have applied its own joining command via the distributed Raft Log");
        nodeD_sm.AppliedCommands.Should().Contain(c => c is DummyCommand && ((DummyCommand)c).Data == "TestDynamic", "Node D must immediately be applying newly replicated business assertions");

        // Cleanup
        foreach (var node in nodes)
        {
            node.Shutdown();
            node.Dispose();
        }
    }
}
