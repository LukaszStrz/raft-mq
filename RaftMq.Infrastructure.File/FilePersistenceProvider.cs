using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RaftMq.Core.Interfaces;
using RaftMq.Core.Models;

namespace RaftMq.Infrastructure.FilePersistence;

public class FilePersistenceProvider<T> : IPersistenceProvider<T> where T : IRaftCommand
{
    private readonly string _stateFilePath;
    private readonly string _logFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public FilePersistenceProvider(string stateFilePath, string logFilePath, JsonSerializerOptions? jsonOptions = null)
    {
        _stateFilePath = stateFilePath;
        _logFilePath = logFilePath;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions();
    }

    public async Task SaveStateAsync(long currentTerm, string? votedFor, CancellationToken cancellationToken = default)
    {
        var state = new { CurrentTerm = currentTerm, VotedFor = votedFor };
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        
        await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(long currentTerm, string? votedFor)> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return (0, null);
        }

        var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken).ConfigureAwait(false);
        var state = JsonSerializer.Deserialize<StateDto>(json, _jsonOptions);
        
        return state != null ? (state.CurrentTerm, state.VotedFor) : (0, null);
    }

    public async Task AppendLogEntriesAsync(IEnumerable<LogEntry<T>> entries, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        using var writer = new StreamWriter(stream);
        
        foreach (var entry in entries)
        {
            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<LogEntry<T>>> LoadLogAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_logFilePath))
        {
            return Array.Empty<LogEntry<T>>();
        }

        var entries = new List<LogEntry<T>>();
        using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            var entry = JsonSerializer.Deserialize<LogEntry<T>>(line, _jsonOptions);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries.AsReadOnly();
    }

    private class StateDto
    {
        public long CurrentTerm { get; set; }
        public string? VotedFor { get; set; }
    }
}
