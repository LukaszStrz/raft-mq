using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RaftMq.Core.Interfaces;

namespace RaftMq.Demo.Console.Domain;

public class KeyValueStateMachine : IStateMachine
{
    private readonly ConcurrentDictionary<string, string> _state = new();
    private readonly ILogger<KeyValueStateMachine> _logger;

    public KeyValueStateMachine(ILogger<KeyValueStateMachine> logger)
    {
        _logger = logger;
    }

    public Task ApplyAsync(IRaftCommand command, CancellationToken cancellationToken = default)
    {
        if (command is KeyValueCommand kv)
        {
            _state[kv.Key] = kv.Value;
            _logger.LogInformation(">>> STATE APPLIED: [{Key}] = '{Value}' <<<", kv.Key, kv.Value);
        }

        return Task.CompletedTask;
    }

    public string? GetValue(string key) => _state.TryGetValue(key, out var value) ? value : null;
}
