using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RaftMq.Core.Models;

namespace RaftMq.Core.Interfaces;

public interface IPersistenceProvider<T> where T : IRaftCommand
{
    Task SaveStateAsync(long currentTerm, string? votedFor, CancellationToken cancellationToken = default);
    Task<(long currentTerm, string? votedFor)> LoadStateAsync(CancellationToken cancellationToken = default);
    
    Task AppendLogEntriesAsync(IEnumerable<LogEntry<T>> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LogEntry<T>>> LoadLogAsync(CancellationToken cancellationToken = default);
}
