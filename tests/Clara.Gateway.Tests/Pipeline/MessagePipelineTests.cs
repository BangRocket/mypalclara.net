using Clara.Core.Config;
using Clara.Gateway.Pipeline;
using Clara.Gateway.Pipeline.Middleware;
using Clara.Gateway.Pipeline.Stages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clara.Gateway.Tests.Pipeline;

public class MessagePipelineTests
{
    [Fact]
    public async Task Stop_phrase_cancels_processing()
    {
        var middleware = new StopPhraseMiddleware(NullLogger<StopPhraseMiddleware>.Instance);
        var context = new PipelineContext
        {
            SessionKey = "test",
            UserId = "user1",
            Content = "clara stop",
        };

        await middleware.InvokeAsync(context);

        Assert.True(context.Cancelled);
    }

    [Theory]
    [InlineData("nevermind")]
    [InlineData("NEVERMIND")]
    [InlineData("never mind")]
    [InlineData("cancel")]
    [InlineData("stop")]
    [InlineData("Clara Stop")]
    public async Task Stop_phrase_variants_cancel_processing(string phrase)
    {
        var middleware = new StopPhraseMiddleware(NullLogger<StopPhraseMiddleware>.Instance);
        var context = new PipelineContext
        {
            SessionKey = "test",
            UserId = "user1",
            Content = phrase,
        };

        await middleware.InvokeAsync(context);

        Assert.True(context.Cancelled);
    }

    [Fact]
    public async Task Normal_message_is_not_cancelled()
    {
        var middleware = new StopPhraseMiddleware(NullLogger<StopPhraseMiddleware>.Instance);
        var context = new PipelineContext
        {
            SessionKey = "test",
            UserId = "user1",
            Content = "Hello Clara, how are you?",
        };

        await middleware.InvokeAsync(context);

        Assert.False(context.Cancelled);
    }

    [Fact]
    public async Task Pipeline_calls_middleware_in_order()
    {
        var callOrder = new List<string>();

        var mw1 = new OrderTrackingMiddleware("first", -10, callOrder);
        var mw2 = new OrderTrackingMiddleware("second", 0, callOrder);
        var mw3 = new OrderTrackingMiddleware("third", 10, callOrder);

        var pipeline = new MessagePipeline(
            [mw3, mw1, mw2], // intentionally out of order
            new StubContextBuildStage(),
            new StubToolSelectionStage(),
            new StubLlmOrchestrationStage(),
            new StubResponseRoutingStage(),
            NullLogger<MessagePipeline>.Instance);

        var context = new PipelineContext { SessionKey = "test", UserId = "u", Content = "hi" };
        await pipeline.ProcessAsync(context);

        Assert.Equal(["first", "second", "third"], callOrder);
    }

    [Fact]
    public async Task Cancelling_middleware_stops_pipeline()
    {
        var callOrder = new List<string>();

        var mw1 = new OrderTrackingMiddleware("first", 0, callOrder);
        var cancel = new CancellingMiddleware(1);
        var mw3 = new OrderTrackingMiddleware("third", 2, callOrder);

        var pipeline = new MessagePipeline(
            [mw1, cancel, mw3],
            new StubContextBuildStage(),
            new StubToolSelectionStage(),
            new StubLlmOrchestrationStage(),
            new StubResponseRoutingStage(),
            NullLogger<MessagePipeline>.Instance);

        var context = new PipelineContext { SessionKey = "test", UserId = "u", Content = "hi" };
        await pipeline.ProcessAsync(context);

        Assert.Equal(["first"], callOrder);
        Assert.True(context.Cancelled);
    }

    // Test helpers

    private class OrderTrackingMiddleware : IPipelineMiddleware
    {
        private readonly string _name;
        private readonly List<string> _callOrder;

        public OrderTrackingMiddleware(string name, int order, List<string> callOrder)
        {
            _name = name;
            Order = order;
            _callOrder = callOrder;
        }

        public int Order { get; }

        public Task InvokeAsync(PipelineContext context, CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return Task.CompletedTask;
        }
    }

    private class CancellingMiddleware : IPipelineMiddleware
    {
        public CancellingMiddleware(int order) => Order = order;
        public int Order { get; }

        public Task InvokeAsync(PipelineContext context, CancellationToken ct = default)
        {
            context.Cancelled = true;
            return Task.CompletedTask;
        }
    }

    // Stub stages that override virtual methods to be no-ops
    private class StubContextBuildStage : ContextBuildStage
    {
        public StubContextBuildStage() : base(null!, null!, NullLogger<ContextBuildStage>.Instance) { }
        public override Task ExecuteAsync(PipelineContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private class StubToolSelectionStage : ToolSelectionStage
    {
        public StubToolSelectionStage() : base(null!, null!, NullLogger<ToolSelectionStage>.Instance) { }
        public override Task ExecuteAsync(PipelineContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private class StubLlmOrchestrationStage : LlmOrchestrationStage
    {
        public StubLlmOrchestrationStage() : base(null!, null!, null!, null!, Options.Create(new GatewayOptions()), NullLogger<LlmOrchestrationStage>.Instance) { }
        public override Task ExecuteAsync(PipelineContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private class StubResponseRoutingStage : ResponseRoutingStage
    {
        public StubResponseRoutingStage() : base(null!, null!, null!, NullLogger<ResponseRoutingStage>.Instance) { }
        public override Task ExecuteAsync(PipelineContext context, CancellationToken ct) => Task.CompletedTask;
    }
}
