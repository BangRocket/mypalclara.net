namespace MyPalClara.Core.Processing;

public interface IMessageProcessor
{
    Task ProcessAsync(ProcessingContext context, CancellationToken ct = default);
}
