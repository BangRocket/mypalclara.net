namespace MyPalClara.Modules.Graph.Models;

public record GraphRelationship(string Id, string SourceId, string TargetId, string Type,
    Dictionary<string, string>? Properties = null);
