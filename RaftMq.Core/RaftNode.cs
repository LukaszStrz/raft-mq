using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;

namespace RaftMq.Core;

// Using the IRaftCommand generic type for the complete node implementation.
public class RaftNode<T> : IDisposable where T : IRaftCommand
{
    private readonly string _nodeId;
    private readonly IReadOnlyList<string> _clusterNodes;
    private readonly IPersistenceProvider<T> _persistenceProvider;
    private readonly IStateMachine _stateMachine;
    private readonly ITransport<T> _transport;
    private readonly ILogger<RaftNode<T>> _logger;

    private RaftNodeState _state = RaftNodeState.Follower;
    private long _currentTerm;
    private string? _votedFor;
    
    private long _commitIndex = 0;
    private long _lastApplied = 0;

    private readonly Dictionary<string, long> _nextIndex = new();
    private readonly Dictionary<string, long> _matchIndex = new();
    private readonly List<LogEntry<T>> _log = new();

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private Timer? _electionTimer;
    private Timer? _heartbeatTimer;
    private readonly Random _random = new();

    private const int HeartbeatIntervalMs = 50;

    public RaftNodeState State => _state;
    public string NodeId => _nodeId;

    public RaftNode(
        string nodeId,
        IReadOnlyList<string> clusterNodes,
        IPersistenceProvider<T> persistenceProvider,
        IStateMachine stateMachine,
        ITransport<T> transport,
        ILogger<RaftNode<T>> logger)
    {
        _nodeId = nodeId;
        _clusterNodes = clusterNodes;
        _persistenceProvider = persistenceProvider;
        _stateMachine = stateMachine;
        _transport = transport;
        _logger = logger;

        _transport.RegisterAppendEntriesHandler(HandleAppendEntriesAsync);
        _transport.RegisterRequestVoteHandler(HandleRequestVoteAsync);
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
            
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            
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

    private void StopElectionTimer()
    {
        _electionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async void OnElectionTimeout(object? stateObj)
    {
        bool lockAcquired = await _stateLock.WaitAsync(0).ConfigureAwait(false);
        if (!lockAcquired) return;
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
        
        long savedTerm = _currentTerm;
        int votes = 1; // Vote for self
        int majority = (_clusterNodes.Count / 2) + 1;

        var tasks = _clusterNodes.Where(n => n != _nodeId).Select(async targetNode => 
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // tight timeout
                var response = await _transport.SendRequestVoteAsync(targetNode, request, cts.Token).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send RequestVote to {TargetNode}", targetNode);
                return null;
            }
        });

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Abort if state changed during wait
            if (_state != RaftNodeState.Candidate || _currentTerm != savedTerm) return;

            foreach (var r in responses)
            {
                if (r == null) continue;
                
                if (r.Term > _currentTerm)
                {
                    await BecomeFollowerAsync(r.Term).ConfigureAwait(false);
                    return;
                }

                if (r.VoteGranted) votes++;
            }

            if (votes >= majority)
            {
                await BecomeLeaderAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _stateLock.Release();
        }
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
        StopHeartbeatTimer();
        string? newVotedFor = (term > _currentTerm) ? null : _votedFor;
        var t = ChangeStateAsync(RaftNodeState.Follower, term, newVotedFor);
        ResetElectionTimer();
        return t;
    }

    private async Task BecomeCandidateAsync()
    {
        await ChangeStateAsync(RaftNodeState.Candidate, _currentTerm + 1, _nodeId).ConfigureAwait(false);
    }

    private async Task BecomeLeaderAsync()
    {
        StopElectionTimer();
        foreach (var node in _clusterNodes)
        {
            if (node == _nodeId) continue;
            _nextIndex[node] = _log.Count > 0 ? _log[^1].Index + 1 : 1;
            _matchIndex[node] = 0;
        }

        await ChangeStateAsync(RaftNodeState.Leader, _currentTerm, _votedFor).ConfigureAwait(false);

        SendHeartbeats();

        _heartbeatTimer ??= new Timer(OnHeartbeatTimeout, null, Timeout.Infinite, Timeout.Infinite);
        _heartbeatTimer.Change(HeartbeatIntervalMs, HeartbeatIntervalMs);
    }

    private void StopHeartbeatTimer()
    {
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async void OnHeartbeatTimeout(object? stateObj)
    {
        bool lockAcquired = await _stateLock.WaitAsync(0).ConfigureAwait(false);
        if (!lockAcquired) return;
        try
        {
            if (_state != RaftNodeState.Leader) return;
            SendHeartbeats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeats");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void SendHeartbeats()
    {
        long lastLogIndex = _log.Count > 0 ? _log[^1].Index : 0;
        long lastLogTerm = _log.Count > 0 ? _log[^1].Term : 0;

        foreach (var targetNode in _clusterNodes)
        {
            if (targetNode == _nodeId) continue;

            var request = new AppendEntriesRequest<T>
            {
                Term = _currentTerm,
                LeaderId = _nodeId,
                PrevLogIndex = lastLogIndex,
                PrevLogTerm = lastLogTerm,
                Entries = Array.Empty<LogEntry<T>>(),
                LeaderCommit = _commitIndex
            };

            _ = SendAppendEntriesUnsafeAsync(targetNode, request);
        }
    }

    private async Task SendAppendEntriesUnsafeAsync(string targetNode, AppendEntriesRequest<T> request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var response = await _transport.SendAppendEntriesAsync(targetNode, request, cts.Token).ConfigureAwait(false);
            
            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_state != RaftNodeState.Leader || request.Term != _currentTerm) return;

                if (response.Term > _currentTerm)
                {
                    await BecomeFollowerAsync(response.Term).ConfigureAwait(false);
                    return;
                }

                if (response.Success)
                {
                    _matchIndex[targetNode] = request.PrevLogIndex + request.Entries.Length;
                    _nextIndex[targetNode] = _matchIndex[targetNode] + 1;

                    AdvanceCommitIndex();
                }
                else
                {
                    if (_nextIndex[targetNode] > 1)
                        _nextIndex[targetNode]--;
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Heartbeat failed to {TargetNode}", targetNode);
        }
    }

    private void AdvanceCommitIndex()
    {
        for (long n = (_log.Count > 0 ? _log[^1].Index : 0); n > _commitIndex; n--)
        {
            int matchCount = 1;
            foreach (var node in _clusterNodes)
            {
                if (node == _nodeId) continue;
                if (_matchIndex.TryGetValue(node, out long mi) && mi >= n)
                    matchCount++;
            }

            if (matchCount >= (_clusterNodes.Count / 2) + 1)
            {
                var entry = _log.First(e => e.Index == n);
                if (entry.Term == _currentTerm)
                {
                    _commitIndex = n;
                    // Apply to state machine (MVP TODO)
                    break;
                }
            }
        }
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
            }

            return new AppendEntriesResponse { Term = _currentTerm, Success = true };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task ApplyCommandAsync(T command, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != RaftNodeState.Leader)
            {
                throw new InvalidOperationException("Only the leader can apply commands.");
            }

            var entry = new LogEntry<T>
            {
                Term = _currentTerm,
                Index = (_log.Count > 0 ? _log[^1].Index : 0) + 1,
                Command = command
            };

            _log.Add(entry);
            await _persistenceProvider.AppendLogEntriesAsync(new[] { entry }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void Shutdown()
    {
        StopElectionTimer();
        StopHeartbeatTimer();
        _transport.StopAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _electionTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _stateLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
