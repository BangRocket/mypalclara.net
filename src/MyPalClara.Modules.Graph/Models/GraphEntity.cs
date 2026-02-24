namespace MyPalClara.Modules.Graph.Models;

public record GraphEntity(string Id, string Name, string Type, Dictionary<string, string>? Properties = null);
