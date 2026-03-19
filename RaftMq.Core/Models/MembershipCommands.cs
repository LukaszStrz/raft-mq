using RaftMq.Core.Interfaces;

namespace RaftMq.Core.Models;

public class AddNodeCommand : IRaftCommand
{
    public string NodeId { get; init; } = string.Empty;
}
