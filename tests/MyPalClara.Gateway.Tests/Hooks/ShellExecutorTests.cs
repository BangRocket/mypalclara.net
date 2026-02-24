using MyPalClara.Gateway.Hooks;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Hooks;

public class ShellExecutorTests
{
    [Fact]
    public void BuildEnvironment_MapsAllGatewayEventFields()
    {
        // Arrange
        var evt = new GatewayEvent(
            Type: "message.received",
            Timestamp: new DateTime(2026, 2, 24, 12, 0, 0, DateTimeKind.Utc),
            NodeId: "node-1",
            Platform: "discord",
            UserId: "user-42",
            ChannelId: "chan-7",
            RequestId: "req-abc",
            Data: new Dictionary<string, object>
            {
                ["content"] = "hello world",
                ["count"] = 5
            });

        // Act
        var env = ShellExecutor.BuildEnvironment(evt);

        // Assert
        Assert.Equal("message.received", env["CLARA_EVENT_TYPE"]);
        Assert.Contains("2026-02-24", env["CLARA_TIMESTAMP"]);
        Assert.Equal("node-1", env["CLARA_NODE_ID"]);
        Assert.Equal("discord", env["CLARA_PLATFORM"]);
        Assert.Equal("user-42", env["CLARA_USER_ID"]);
        Assert.Equal("chan-7", env["CLARA_CHANNEL_ID"]);
        Assert.Equal("req-abc", env["CLARA_REQUEST_ID"]);
        Assert.True(env.ContainsKey("CLARA_EVENT_DATA"));
        Assert.Equal("hello world", env["CLARA_CONTENT"]);
        Assert.Equal("5", env["CLARA_COUNT"]);
    }

    [Fact]
    public void BuildEnvironment_NullOptionalFields_OmitsKeys()
    {
        // Arrange — minimal event with no optional fields
        var evt = new GatewayEvent("minimal.event", DateTime.UtcNow);

        // Act
        var env = ShellExecutor.BuildEnvironment(evt);

        // Assert
        Assert.Equal("minimal.event", env["CLARA_EVENT_TYPE"]);
        Assert.False(env.ContainsKey("CLARA_NODE_ID"));
        Assert.False(env.ContainsKey("CLARA_PLATFORM"));
        Assert.False(env.ContainsKey("CLARA_USER_ID"));
        Assert.False(env.ContainsKey("CLARA_CHANNEL_ID"));
        Assert.False(env.ContainsKey("CLARA_REQUEST_ID"));
        Assert.False(env.ContainsKey("CLARA_EVENT_DATA"));
    }

    [Fact]
    public void ExpandVariables_SubstitutesPlaceholders()
    {
        // Arrange
        var env = new Dictionary<string, string>
        {
            ["CLARA_EVENT_TYPE"] = "test.event",
            ["CLARA_USER_ID"] = "user-42",
            ["CLARA_PLATFORM"] = "discord"
        };
        var command = "echo ${CLARA_EVENT_TYPE} from ${CLARA_USER_ID} on ${CLARA_PLATFORM}";

        // Act
        var result = ShellExecutor.ExpandVariables(command, env);

        // Assert
        Assert.Equal("echo test.event from user-42 on discord", result);
    }

    [Fact]
    public void ExpandVariables_NoPlaceholders_ReturnsUnchanged()
    {
        // Arrange
        var env = new Dictionary<string, string> { ["CLARA_EVENT_TYPE"] = "test" };
        var command = "echo hello world";

        // Act
        var result = ShellExecutor.ExpandVariables(command, env);

        // Assert
        Assert.Equal("echo hello world", result);
    }

    [Fact]
    public async Task ExecuteAsync_EchoCommand_ReturnsOutput()
    {
        // Arrange
        var evt = new GatewayEvent("test.exec", DateTime.UtcNow);

        // Act
        var (success, output, error) = await ShellExecutor.ExecuteAsync("echo hello", evt);

        // Assert
        Assert.True(success);
        Assert.Equal("hello", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task ExecuteAsync_FailingCommand_ReturnsFalse()
    {
        // Arrange
        var evt = new GatewayEvent("test.fail", DateTime.UtcNow);

        // Act
        var (success, output, error) = await ShellExecutor.ExecuteAsync("exit 1", evt);

        // Assert
        Assert.False(success);
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithEnvVar_SubstitutesVariable()
    {
        // Arrange
        var evt = new GatewayEvent("test.env", DateTime.UtcNow, UserId: "josh");

        // Act
        var (success, output, _) = await ShellExecutor.ExecuteAsync(
            "echo ${CLARA_USER_ID}", evt);

        // Assert
        Assert.True(success);
        Assert.Equal("josh", output);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_KillsProcessAndReturnsFalse()
    {
        // Arrange
        var evt = new GatewayEvent("test.timeout", DateTime.UtcNow);

        // Act — sleep for 30 seconds but timeout after 1 second
        var (success, _, error) = await ShellExecutor.ExecuteAsync(
            "sleep 30", evt, timeoutSeconds: 1.0);

        // Assert
        Assert.False(success);
        Assert.Equal("Timed out", error);
    }
}
