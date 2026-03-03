namespace Clara.Gateway.Queues;

public class QueueMetrics
{
    private int _totalEnqueued;
    private int _totalProcessed;

    public void RecordEnqueue() => Interlocked.Increment(ref _totalEnqueued);
    public void RecordProcessed() => Interlocked.Increment(ref _totalProcessed);
    public int TotalEnqueued => _totalEnqueued;
    public int TotalProcessed => _totalProcessed;
    public int Pending => _totalEnqueued - _totalProcessed;
}
