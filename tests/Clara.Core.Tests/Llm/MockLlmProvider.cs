using System.Runtime.CompilerServices;
using Clara.Core.Llm;

namespace Clara.Core.Tests.Llm;

/// <summary>
/// A mock ILlmProvider that returns a predetermined response.
/// </summary>
internal class MockLlmProvider : ILlmProvider
{
    private readonly string _responseText;

    public string Name => "mock";

    public MockLlmProvider(string responseText)
    {
        _responseText = responseText;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var response = new LlmResponse(
            [new TextContent(_responseText)],
            "EndTurn",
            new LlmUsage(100, 50));

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new LlmStreamChunk { TextDelta = _responseText };
        await Task.CompletedTask;
    }
}
