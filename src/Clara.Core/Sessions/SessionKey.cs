namespace Clara.Core.Sessions;

public record SessionKey
{
    public string AgentId { get; init; } = "main";
    public string Platform { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Identifier { get; init; } = "";
    public string? SubTaskId { get; init; }

    public override string ToString() =>
        SubTaskId is not null
            ? $"clara:sub:{AgentId}:{Platform}:{Scope}:{Identifier}:{SubTaskId}"
            : $"clara:{AgentId}:{Platform}:{Scope}:{Identifier}";

    public static SessionKey Parse(string key)
    {
        var parts = key.Split(':');

        if (parts.Length >= 7 && parts[0] == "clara" && parts[1] == "sub")
            return new SessionKey
            {
                AgentId = parts[2],
                Platform = parts[3],
                Scope = parts[4],
                Identifier = parts[5],
                SubTaskId = parts[6]
            };

        if (parts.Length >= 5 && parts[0] == "clara")
            return new SessionKey
            {
                AgentId = parts[1],
                Platform = parts[2],
                Scope = parts[3],
                Identifier = parts[4]
            };

        throw new FormatException($"Invalid session key: {key}");
    }

    public bool IsSubAgent => SubTaskId is not null;
    public string ParentKey => $"clara:{AgentId}:{Platform}:{Scope}:{Identifier}";
    public string LaneKey => IsSubAgent ? ToString() : ParentKey;
}
