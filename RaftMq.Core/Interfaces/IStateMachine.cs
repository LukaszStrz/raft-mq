using System.Threading;
using System.Threading.Tasks;

namespace RaftMq.Core.Interfaces;

public interface IStateMachine
{
    Task ApplyAsync(IRaftCommand command, CancellationToken cancellationToken = default);
}
