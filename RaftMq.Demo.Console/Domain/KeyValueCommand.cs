using RaftMq.Core.Interfaces;

namespace RaftMq.Demo.Console.Domain;

public class KeyValueCommand : IRaftCommand
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
