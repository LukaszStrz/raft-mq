using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;

namespace RaftMq.Core;

public class RaftNode<T> : IDisposable where T : IRaftCommand
{
    private readonly string _nodeId;
    private readonly IReadOnlyList<string> _clusterNodes;
    private readonly IPersistenceProvider<T> _persistenceProvider;
    private readonly IStateMachine _stateMachine;
    private readonly ILogger<RaftNode<T>> _logger;

    private RaftNodeState _state = RaftNodeState.Follower;
    private long _currentTerm;
    private string? _votedFor;
    
    // Volatile state on all servers
    private long _commitIndex = 0;
    private long _lastApplied = 0;

    // Volatile state on leaders
    private readonly Dictionary<string, long> _nextIndex = new();
    private readonly Dictionary<string, long> _matchIndex = new();

    // Log entries (in-memory cache)
    private readonly List<LogEntry<T>> _log = new();

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private Timer? _electionTimer;
    private readonly Random _random = new();

    public RaftNode(
        string nodeId,
        IReadOnlyList<string> clusterNodes,
        IPersistenceProvider<T> persistenceProvider,
        IStateMachine stateMachine,
        ILogger<RaftNode<T>> logger)
    {
        _nodeId = nodeId;
        _clusterNodes = clusterNodes;
        _persistenceProvider = persistenceProvider;
        _stateMachine = stateMachine;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (term, votedFor) = await _persistenceProvider.LoadStateAsync(cancellationToken).ConfigureAwait(false);
            _currentTerm = term;
            _votedFor = votedFor;

            var entries = await _persistenceProvider.LoadLogAsync(cancellationToken).ConfigureAwait(false);
            _log.AddRange(entries);
            
            _logger.LogInformation("Node {NodeId} initialized. Term: {CurrentTerm}, State: {State}, Log length: {LogLength}", 
                _nodeId, _currentTerm, _state, _log.Count);

            ResetElectionTimer();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void ResetElectionTimer()
    {
        if (_state == RaftNodeState.Leader) return;

        int timeout = _random.Next(150, 301); // 150-300ms
        _electionTimer ??= new Timer(OnElectionTimeout, null, Timeout.Infinite, Timeout.Infinite);
        _electionTimer.Change(timeout, Timeout.Infinite);
    }

    private async void OnElectionTimeout(object? stateObj)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartElectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting election on node {NodeId}", _nodeId);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task StartElectionAsync()
    {
        await BecomeCandidateAsync().ConfigureAwait(false);
        ResetElectionTimer();
        
        long lastLogIndex = _log.Count > 0 ? _log[^1].Index : 0;
        long lastLogTerm = _log.Count > 0 ? _log[^1].Term : 0;

        var request = new RequestVoteRequest
        {
            Term = _currentTerm,
            CandidateId = _nodeId,
            LastLogIndex = lastLogIndex,
            LastLogTerm = lastLogTerm
        };

        _logger.LogInformation("Node {NodeId} started election for term {Term}", _nodeId, _currentTerm);
        // Transport: broadcast request to cluster (to be implemented in Phase 3)
    }

    private async Task ChangeStateAsync(RaftNodeState newState, long term, string? votedFor)
    {
        bool stateChanged = _state != newState || _currentTerm != term || _votedFor != votedFor;
        
        _state = newState;
        _currentTerm = term;
        _votedFor = votedFor;

        if (stateChanged)
        {
            await _persistenceProvider.SaveStateAsync(_currentTerm, _votedFor).ConfigureAwait(false);
            _logger.LogInformation("Node {NodeId} changed to {State} at Term {Term} (VotedFor: {VotedFor})", 
                _nodeId, _state, _currentTerm, _votedFor ?? "null");
        }
    }

    private Task BecomeFollowerAsync(long term)
    {
        string? newVotedFor = (term > _currentTerm) ? null : _votedFor;
        return ChangeStateAsync(RaftNodeState.Follower, term, newVotedFor);
    }

    private async Task BecomeCandidateAsync()
    {
        await ChangeStateAsync(RaftNodeState.Candidate, _currentTerm + 1, _nodeId).ConfigureAwait(false);
    }

    private Task BecomeLeaderAsync()
    {
        foreach (var node in _clusterNodes)
        {
            if (node == _nodeId) continue;
            _nextIndex[node] = _log.Count > 0 ? _log[^1].Index + 1 : 1;
            _matchIndex[node] = 0;
        }

        return ChangeStateAsync(RaftNodeState.Leader, _currentTerm, _votedFor);
    }

    public async Task<RequestVoteResponse> HandleRequestVoteAsync(RequestVoteRequest request)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (request.Term > _currentTerm)
            {
                await BecomeFollowerAsync(request.Term).ConfigureAwait(false);
            }

            bool voteGranted = false;
            if (request.Term == _currentTerm && (_votedFor == null || _votedFor == request.CandidateId))
            {
                long lastLogIndex = _log.Count > 0 ? _log[^1].Index : 0;
                long lastLogTerm = _log.Count > 0 ? _log[^1].Term : 0;

                if (request.LastLogTerm > lastLogTerm || 
                   (request.LastLogTerm == lastLogTerm && request.LastLogIndex >= lastLogIndex))
                {
                    voteGranted = true;
                    await ChangeStateAsync(_state, _currentTerm, request.CandidateId).ConfigureAwait(false);
                    ResetElectionTimer();
                }
            }

            return new RequestVoteResponse
            {
                Term = _currentTerm,
                VoteGranted = voteGranted
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<AppendEntriesResponse> HandleAppendEntriesAsync(AppendEntriesRequest<T> request)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (request.Term > _currentTerm)
            {
                await BecomeFollowerAsync(request.Term).ConfigureAwait(false);
            }

            if (request.Term < _currentTerm)
            {
                return new AppendEntriesResponse { Term = _currentTerm, Success = false };
            }

            ResetElectionTimer();

            if (_state == RaftNodeState.Candidate)
            {
                await BecomeFollowerAsync(request.Term).ConfigureAwait(false);
            }

            if (request.PrevLogIndex > 0)
            {
                if (_log.Count < request.PrevLogIndex)
                    return new AppendEntriesResponse { Term = _currentTerm, Success = false };
                
                if (_log[(int)request.PrevLogIndex - 1].Term != request.PrevLogTerm)
                    return new AppendEntriesResponse { Term = _currentTerm, Success = false };
            }

            int logInsertIndex = (int)request.PrevLogIndex;
            var newEntriesToPersist = new List<LogEntry<T>>();
            
            for (int i = 0; i < request.Entries.Length; i++)
            {
                var newEntry = request.Entries[i];
                if (logInsertIndex < _log.Count)
                {
                    if (_log[logInsertIndex].Term != newEntry.Term)
                    {
                        _log.RemoveRange(logInsertIndex, _log.Count - logInsertIndex);
                        _log.Add(newEntry);
                        newEntriesToPersist.Add(newEntry);
                    }
                }
                else
                {
                    _log.Add(newEntry);
                    newEntriesToPersist.Add(newEntry);
                }
                logInsertIndex++;
            }

            if (newEntriesToPersist.Count > 0)
            {
                await _persistenceProvider.AppendLogEntriesAsync(newEntriesToPersist).ConfigureAwait(false);
            }

            if (request.LeaderCommit > _commitIndex)
            {
                _commitIndex = Math.Min(request.LeaderCommit, _log.Count > 0 ? _log[^1].Index : 0);
                
                // TODO: Apply logs to state machine
            }

            return new AppendEntriesResponse { Term = _currentTerm, Success = true };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void Dispose()
    {
        _electionTimer?.Dispose();
        _stateLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
