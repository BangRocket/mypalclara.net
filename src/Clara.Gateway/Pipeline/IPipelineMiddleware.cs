namespace Clara.Gateway.Pipeline;

public interface IPipelineMiddleware
{
    int Order { get; }
    Task InvokeAsync(PipelineContext context, CancellationToken ct = default);
}
