using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Graph.Client;
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Modules.Graph.Extraction;

public class TripleExtractor
{
    private readonly ILlmProvider _llm;
    private readonly GraphOperations _graphOps;
    private readonly ILogger<TripleExtractor> _logger;

    public TripleExtractor(ILlmProvider llm, GraphOperations graphOps, ILogger<TripleExtractor> logger)
    {
        _llm = llm;
        _graphOps = graphOps;
        _logger = logger;
    }

    public async Task ExtractAndStoreAsync(string userMessage, string assistantMessage,
        CancellationToken ct = default)
    {
        var messages = ExtractionPrompt.Build(userMessage, assistantMessage);
        var response = await _llm.InvokeAsync(messages, ct: ct);
        var content = response.Content ?? "[]";

        try
        {
            var triples = JsonSerializer.Deserialize<List<GraphTriple>>(content) ?? [];
            foreach (var triple in triples)
            {
                await _graphOps.UpsertRelationshipAsync(triple.Subject, triple.Predicate, triple.Object, ct);
            }
            _logger.LogInformation("Extracted {Count} triples", triples.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response: {Content}", content);
        }
    }
}
