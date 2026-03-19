using System;
using System.Collections.Generic;
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
    private readonly IPersistenceProvider<DummyCommand> _persistenceProvider;
    private readonly IStateMachine _stateMachine;
    private readonly ITransport<DummyCommand> _transport;
    private readonly ILogger<RaftNode<DummyCommand>> _logger;

    public RaftNodeTests()
    {
        _fixture = new Fixture();
        _persistenceProvider = Substitute.For<IPersistenceProvider<DummyCommand>>();
        _stateMachine = Substitute.For<IStateMachine>();
        _transport = Substitute.For<ITransport<DummyCommand>>();
        _logger = Substitute.For<ILogger<RaftNode<DummyCommand>>>();
        
        _persistenceProvider.LoadStateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(long, string?)>((0, null)));
            
        _persistenceProvider.LoadLogAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogEntry<DummyCommand>>>(new List<LogEntry<DummyCommand>>()));
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadState_AndSetFollower()
    {
        // Arrange
        var node = new RaftNode<DummyCommand>("node1", new[] { "node1", "node2" }, _persistenceProvider, _stateMachine, _transport, _logger);

        // Act
        await node.InitializeAsync();

        // Assert
        node.State.Should().Be(RaftNodeState.Follower);
        await _persistenceProvider.Received(1).LoadStateAsync(Arg.Any<CancellationToken>());
        await _transport.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAppendEntries_WhenTermIsGreater_ShouldBecomeFollower()
    {
        // Arrange
        var node = new RaftNode<DummyCommand>("node1", new[] { "node1", "node2" }, _persistenceProvider, _stateMachine, _transport, _logger);
        await node.InitializeAsync();
        
        var request = new AppendEntriesRequest<DummyCommand>
        {
            Term = 5,
            LeaderId = "node2",
            Entries = Array.Empty<LogEntry<DummyCommand>>()
        };

        // Act
        var response = await node.HandleAppendEntriesAsync(request);

        // Assert
        response.Success.Should().BeTrue();
        response.Term.Should().Be(5);
        node.State.Should().Be(RaftNodeState.Follower);
    }
}
