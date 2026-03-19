using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RaftMq.Core;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;
using Xunit;

namespace RaftMq.Tests.Unit;

public class DummyCommand : IRaftCommand { }

public class RaftNodeTests
{
    private readonly IFixture _fixture;
    private readonly IPersistenceProvider<IRaftCommand> _persistenceProvider;
    private readonly IStateMachine _stateMachine;
    private readonly ITransport<IRaftCommand> _transport;
    private readonly ILogger<RaftNode<IRaftCommand>> _logger;

    public RaftNodeTests()
    {
        _fixture = new Fixture();
        _persistenceProvider = Substitute.For<IPersistenceProvider<IRaftCommand>>();
        _stateMachine = Substitute.For<IStateMachine>();
        _transport = Substitute.For<ITransport<IRaftCommand>>();
        _logger = Substitute.For<ILogger<RaftNode<IRaftCommand>>>();
        
        _persistenceProvider.LoadStateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(long, string?)>((0, null)));
            
        _persistenceProvider.LoadLogAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogEntry<IRaftCommand>>>(new List<LogEntry<IRaftCommand>>()));
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadState_AndSetFollower()
    {
        var node = new RaftNode<IRaftCommand>("node1", new[] { "node1", "node2" }, _persistenceProvider, _stateMachine, _transport, _logger);
        await node.InitializeAsync();

        node.State.Should().Be(RaftNodeState.Follower);
        await _persistenceProvider.Received(1).LoadStateAsync(Arg.Any<CancellationToken>());
        await _transport.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAppendEntries_WhenTermIsGreater_ShouldBecomeFollower()
    {
        var node = new RaftNode<IRaftCommand>("node1", new[] { "node1", "node2" }, _persistenceProvider, _stateMachine, _transport, _logger);
        await node.InitializeAsync();
        
        var request = new AppendEntriesRequest<IRaftCommand>
        {
            Term = 5,
            LeaderId = "node2",
            Entries = Array.Empty<LogEntry<IRaftCommand>>()
        };

        var response = await node.HandleAppendEntriesAsync(request);

        response.Success.Should().BeTrue();
        response.Term.Should().Be(5);
        node.State.Should().Be(RaftNodeState.Follower);
    }

    [Fact]
    public async Task Leader_OnNodeDiscovered_Should_Propose_AddNodeCommand()
    {
        var node = new RaftNode<IRaftCommand>("node1", new[] { "node1", "node2" }, _persistenceProvider, _stateMachine, _transport, _logger);
        await node.InitializeAsync();

        var stateField = typeof(RaftNode<IRaftCommand>).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
        stateField!.SetValue(node, RaftNodeState.Leader);

        _transport.OnNodeDiscovered += Raise.Event<EventHandler<string>>(_transport, "newNode3");

        await Task.Delay(250); // Ensure the background async handler processes the lock and executes logic

        await _persistenceProvider.Received(1).AppendLogEntriesAsync(
            Arg.Is<IEnumerable<LogEntry<IRaftCommand>>>(entries => 
                entries.Any(e => e.Command is AddNodeCommand && ((AddNodeCommand)e.Command).NodeId == "newNode3")), 
            Arg.Any<CancellationToken>());
    }
}
