using System.Security.Cryptography;
using System.Text;

namespace Clara.Core.Llm.ToolCalling;

public class ToolLoopDetector
{
    private readonly int _maxIdenticalCalls;
    private readonly int _maxTotalRounds;
    private readonly List<ToolCallRecord> _history = [];

    public ToolLoopDetector(int maxIdenticalCalls = 3, int maxTotalRounds = 10)
    {
        _maxIdenticalCalls = maxIdenticalCalls;
        _maxTotalRounds = maxTotalRounds;
    }

    public void Record(string name, string argumentsJson, int round)
    {
        var hash = GetHash(argumentsJson);
        _history.Add(new ToolCallRecord(name, hash, round));
    }

    public bool IsLoop(string name, string argumentsJson)
    {
        var hash = GetHash(argumentsJson);

        // Check identical calls
        var identicalCount = _history.Count(h => h.Name == name && h.ArgumentsHash == hash);
        if (identicalCount >= _maxIdenticalCalls) return true;

        // Check circular pattern (A->B->A->B)
        if (_history.Count >= 4)
        {
            var recent = _history.TakeLast(4).ToList();
            if (recent[0].Name == recent[2].Name && recent[1].Name == recent[3].Name &&
                recent[0].Name != recent[1].Name)
                return true;
        }

        return false;
    }

    public bool IsMaxRoundsReached(int currentRound) => currentRound >= _maxTotalRounds;

    public void Reset() => _history.Clear();

    private static string GetHash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
}
