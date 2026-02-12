using System.Text.Json;

namespace MyPalClara.Tools.Backfill;

/// <summary>
/// Tracks completed conversations so backfill can resume after interruption.
/// Writes to a JSON file after each conversation completes.
/// </summary>
public sealed class BackfillCheckpoint
{
    private readonly string _filePath;
    private readonly HashSet<string> _completed;
    private DateTime _lastRunAt;

    public IReadOnlySet<string> CompletedConversations => _completed;

    public BackfillCheckpoint(string filePath)
    {
        _filePath = filePath;
        _completed = [];
        _lastRunAt = DateTime.UtcNow;

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<CheckpointData>(json);
                if (data?.CompletedConversations is not null)
                {
                    foreach (var id in data.CompletedConversations)
                        _completed.Add(id);
                }
                if (data?.LastRunAt is not null)
                    _lastRunAt = data.LastRunAt.Value;
            }
            catch
            {
                // Corrupted checkpoint — start fresh
            }
        }
    }

    public bool IsCompleted(string sourceId) => _completed.Contains(sourceId);

    public void MarkCompleted(string sourceId)
    {
        _completed.Add(sourceId);
        _lastRunAt = DateTime.UtcNow;
        Save();
    }

    public void Reset()
    {
        _completed.Clear();
        _lastRunAt = DateTime.UtcNow;
        Save();
    }

    private void Save()
    {
        var data = new CheckpointData
        {
            CompletedConversations = _completed.Order().ToList(),
            LastRunAt = _lastRunAt,
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private sealed class CheckpointData
    {
        public List<string>? CompletedConversations { get; set; }
        public DateTime? LastRunAt { get; set; }
    }
}
