using System;
using System.Threading;
using System.Threading.Tasks;
using RaftMq.Core.Models;

namespace RaftMq.Core.Interfaces;

public interface ITransport<T> where T : IRaftCommand
{
    Task<AppendEntriesResponse> SendAppendEntriesAsync(string targetNodeId, AppendEntriesRequest<T> request, CancellationToken cancellationToken = default);
    Task<RequestVoteResponse> SendRequestVoteAsync(string targetNodeId, RequestVoteRequest request, CancellationToken cancellationToken = default);
    
    event EventHandler<string>? OnNodeDiscovered;

    void RegisterAppendEntriesHandler(Func<AppendEntriesRequest<T>, Task<AppendEntriesResponse>> handler);
    void RegisterRequestVoteHandler(Func<RequestVoteRequest, Task<RequestVoteResponse>> handler);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
