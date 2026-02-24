namespace MyPalClara.Modules.Sdk;

public static class EventTypes
{
    // Lifecycle
    public const string GatewayStartup = "gateway:startup";
    public const string GatewayShutdown = "gateway:shutdown";

    // Adapters
    public const string AdapterConnected = "adapter:connected";
    public const string AdapterDisconnected = "adapter:disconnected";

    // Sessions
    public const string SessionStart = "session:start";
    public const string SessionEnd = "session:end";
    public const string SessionTimeout = "session:timeout";

    // Messages
    public const string MessageReceived = "message:received";
    public const string MessageSent = "message:sent";
    public const string MessageCancelled = "message:cancelled";

    // Tools
    public const string ToolStart = "tool:start";
    public const string ToolEnd = "tool:end";
    public const string ToolError = "tool:error";

    // Scheduler
    public const string ScheduledTaskRun = "scheduler:task_run";
    public const string ScheduledTaskError = "scheduler:task_error";

    // Memory
    public const string MemoryRead = "memory:read";
    public const string MemoryWrite = "memory:write";
}
