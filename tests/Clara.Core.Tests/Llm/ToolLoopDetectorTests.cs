using Clara.Core.Llm.ToolCalling;

namespace Clara.Core.Tests.Llm;

public class ToolLoopDetectorTests
{
    [Fact]
    public void No_loop_initially()
    {
        var detector = new ToolLoopDetector();

        Assert.False(detector.IsLoop("shell_execute", """{"command":"echo hi"}"""));
    }

    [Fact]
    public void Detects_identical_calls_after_threshold()
    {
        var detector = new ToolLoopDetector(maxIdenticalCalls: 3);
        var args = """{"command":"echo hi"}""";

        detector.Record("shell_execute", args, 0);
        detector.Record("shell_execute", args, 1);
        detector.Record("shell_execute", args, 2);

        // 3 recorded, next identical → loop
        Assert.True(detector.IsLoop("shell_execute", args));
    }

    [Fact]
    public void Does_not_trigger_below_threshold()
    {
        var detector = new ToolLoopDetector(maxIdenticalCalls: 3);
        var args = """{"command":"echo hi"}""";

        detector.Record("shell_execute", args, 0);
        detector.Record("shell_execute", args, 1);

        // Only 2 recorded → not yet a loop
        Assert.False(detector.IsLoop("shell_execute", args));
    }

    [Fact]
    public void Different_args_are_not_identical()
    {
        var detector = new ToolLoopDetector(maxIdenticalCalls: 3);

        detector.Record("shell_execute", """{"command":"echo 1"}""", 0);
        detector.Record("shell_execute", """{"command":"echo 2"}""", 1);
        detector.Record("shell_execute", """{"command":"echo 3"}""", 2);

        Assert.False(detector.IsLoop("shell_execute", """{"command":"echo 4"}"""));
    }

    [Fact]
    public void Detects_circular_pattern_ABAB()
    {
        var detector = new ToolLoopDetector();

        detector.Record("tool_a", """{"x":1}""", 0);
        detector.Record("tool_b", """{"y":2}""", 1);
        detector.Record("tool_a", """{"x":1}""", 2);
        detector.Record("tool_b", """{"y":2}""", 3);

        // A->B->A->B pattern → loop on next call
        Assert.True(detector.IsLoop("anything", """{}"""));
    }

    [Fact]
    public void Max_rounds_reached()
    {
        var detector = new ToolLoopDetector(maxTotalRounds: 5);

        Assert.False(detector.IsMaxRoundsReached(4));
        Assert.True(detector.IsMaxRoundsReached(5));
        Assert.True(detector.IsMaxRoundsReached(6));
    }

    [Fact]
    public void Reset_clears_history()
    {
        var detector = new ToolLoopDetector(maxIdenticalCalls: 3);
        var args = """{"command":"echo hi"}""";

        detector.Record("shell_execute", args, 0);
        detector.Record("shell_execute", args, 1);
        detector.Record("shell_execute", args, 2);

        Assert.True(detector.IsLoop("shell_execute", args));

        detector.Reset();

        Assert.False(detector.IsLoop("shell_execute", args));
    }
}
