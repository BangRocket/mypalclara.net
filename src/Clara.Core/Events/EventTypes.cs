namespace Clara.Core.Events;

public static class SessionEvents
{
    public const string Start = "session:start";
    public const string End = "session:end";
    public const string Timeout = "session:timeout";
}

public static class MessageEvents
{
    public const string Received = "message:received";
    public const string Sent = "message:sent";
    public const string Cancelled = "message:cancelled";
}

public static class ToolEvents
{
    public const string Start = "tool:start";
    public const string End = "tool:end";
    public const string Error = "tool:error";
}

public static class AdapterEvents
{
    public const string Connected = "adapter:connected";
    public const string Disconnected = "adapter:disconnected";
}

public static class MemoryEvents
{
    public const string Read = "memory:read";
    public const string Write = "memory:write";
}

public static class LifecycleEvents
{
    public const string Startup = "gateway:startup";
    public const string Shutdown = "gateway:shutdown";
}

public static class SchedulerEvents
{
    public const string TaskRun = "scheduler:task_run";
    public const string TaskError = "scheduler:task_error";
}

public static class SubAgentEvents
{
    public const string Completed = "subagent:completed";
}

public static class HeartbeatEvents
{
    public const string Action = "heartbeat:action";
}
