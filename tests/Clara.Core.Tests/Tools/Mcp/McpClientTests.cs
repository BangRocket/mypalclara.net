using System.Text.Json;
using Clara.Core.Tools;
using Clara.Core.Tools.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clara.Core.Tests.Tools.Mcp;

public class McpServerManagerTests
{
    private McpServerManager CreateManager() =>
        new(NullLoggerFactory.Instance);

    [Fact]
    public void GetRunningServers_initially_empty()
    {
        var manager = CreateManager();

        var servers = manager.GetRunningServers();

        Assert.Empty(servers);
    }

    [Fact]
    public void IsRunning_returns_false_for_unknown_server()
    {
        var manager = CreateManager();

        var running = manager.IsRunning("nonexistent");

        Assert.False(running);
    }

    [Fact]
    public async Task StartServerAsync_throws_for_nonexistent_command()
    {
        var manager = CreateManager();

        // Starting a server with a nonexistent command should throw
        await Assert.ThrowsAnyAsync<Exception>(
            () => manager.StartServerAsync("test", "nonexistent_command_that_does_not_exist_12345"));
    }

    [Fact]
    public async Task StopServerAsync_throws_for_unknown_server()
    {
        var manager = CreateManager();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.StopServerAsync("nonexistent"));
    }

    [Fact]
    public async Task GetToolsAsync_throws_for_unknown_server()
    {
        var manager = CreateManager();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.GetToolsAsync("nonexistent"));
    }

    [Fact]
    public async Task CallToolAsync_throws_for_unknown_server()
    {
        var manager = CreateManager();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CallToolAsync("nonexistent", "some_tool", "{}"));
    }

    [Fact]
    public async Task StopAllAsync_succeeds_when_empty()
    {
        var manager = CreateManager();

        // Should not throw
        await manager.StopAllAsync();
    }
}

public class McpRegistryAdapterTests
{
    [Fact]
    public void McpToolWrapper_has_correct_namespaced_name()
    {
        var manager = new McpServerManager(NullLoggerFactory.Instance);
        var toolInfo = new McpToolInfo("search", "Search the web", """{"type":"object","properties":{"query":{"type":"string"}}}""");

        var wrapper = new McpToolWrapper(manager, "brave", toolInfo);

        Assert.Equal("mcp_brave_search", wrapper.Name);
        Assert.Equal("Search the web", wrapper.Description);
        Assert.Equal(ToolCategory.Mcp, wrapper.Category);
        Assert.Equal("brave", wrapper.ServerName);
        Assert.Equal("search", wrapper.OriginalToolName);
    }

    [Fact]
    public void McpToolWrapper_parses_parameter_schema()
    {
        var manager = new McpServerManager(NullLoggerFactory.Instance);
        var schemaJson = """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""";
        var toolInfo = new McpToolInfo("search", "Search", schemaJson);

        var wrapper = new McpToolWrapper(manager, "test", toolInfo);
        var schema = wrapper.ParameterSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _));
    }

    [Fact]
    public async Task McpToolWrapper_execute_returns_fail_when_server_not_running()
    {
        var manager = new McpServerManager(NullLoggerFactory.Instance);
        var toolInfo = new McpToolInfo("search", "Search", "{}");
        var wrapper = new McpToolWrapper(manager, "offline_server", toolInfo);
        var context = new ToolExecutionContext("user1", "session1", "test", false, null);
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await wrapper.ExecuteAsync(args, context);

        Assert.False(result.Success);
        Assert.Contains("not running", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void McpToolWrapper_multiple_servers_different_namespaces()
    {
        var manager = new McpServerManager(NullLoggerFactory.Instance);

        var tool1 = new McpToolWrapper(manager, "server_a", new McpToolInfo("read", "Read", "{}"));
        var tool2 = new McpToolWrapper(manager, "server_b", new McpToolInfo("read", "Read", "{}"));

        // Same tool name on different servers should produce different namespaced names
        Assert.NotEqual(tool1.Name, tool2.Name);
        Assert.Equal("mcp_server_a_read", tool1.Name);
        Assert.Equal("mcp_server_b_read", tool2.Name);
    }
}

public class McpInstallerTests
{
    [Theory]
    [InlineData("smithery:brave-search", McpSourceType.Smithery, "brave-search")]
    [InlineData("Smithery:my-package", McpSourceType.Smithery, "my-package")]
    [InlineData("npm:@modelcontextprotocol/server-brave", McpSourceType.Npm, "@modelcontextprotocol/server-brave")]
    [InlineData("npx:@scope/package", McpSourceType.Npm, "@scope/package")]
    [InlineData("/usr/local/bin/my-mcp-server", McpSourceType.Local, "/usr/local/bin/my-mcp-server")]
    [InlineData("./relative/path/server", McpSourceType.Local, "./relative/path/server")]
    [InlineData("../parent/server", McpSourceType.Local, "../parent/server")]
    [InlineData("@modelcontextprotocol/server-brave", McpSourceType.Npm, "@modelcontextprotocol/server-brave")]
    public void ParseSource_detects_correct_type(string source, McpSourceType expectedType, string expectedPackage)
    {
        var (type, package) = McpInstaller.ParseSource(source);

        Assert.Equal(expectedType, type);
        Assert.Equal(expectedPackage, package);
    }

    [Theory]
    [InlineData("some-command --flag")]
    [InlineData("python server.py")]
    public void ParseSource_falls_back_to_raw(string source)
    {
        var (type, _) = McpInstaller.ParseSource(source);

        Assert.Equal(McpSourceType.Raw, type);
    }

    [Fact]
    public async Task InstallAsync_npm_source_creates_npx_command()
    {
        var installer = new McpInstaller();

        var result = await installer.InstallAsync("npm:@modelcontextprotocol/server-brave", "brave");

        Assert.True(result.Success);
        Assert.Equal("brave", result.ServerName);
        Assert.Equal("npx", result.Command);
        Assert.Contains("@modelcontextprotocol/server-brave", result.Args);
    }

    [Fact]
    public async Task InstallAsync_local_path_uses_path_as_command()
    {
        var installer = new McpInstaller();

        var result = await installer.InstallAsync("/usr/local/bin/my-server", "local-mcp");

        Assert.True(result.Success);
        Assert.Equal("local-mcp", result.ServerName);
        Assert.Equal("/usr/local/bin/my-server", result.Command);
    }

    [Fact]
    public async Task InstallAsync_generates_name_from_package()
    {
        var installer = new McpInstaller();

        var result = await installer.InstallAsync("npm:@scope/my-mcp-server");

        Assert.True(result.Success);
        Assert.Equal("scope_my-mcp-server", result.ServerName);
    }

    [Fact]
    public async Task InstallAsync_raw_command_splits_into_command_and_args()
    {
        var installer = new McpInstaller();

        var result = await installer.InstallAsync("python server.py --port 8080");

        Assert.True(result.Success);
        Assert.Equal("python", result.Command);
        Assert.Equal("server.py --port 8080", result.Args);
    }
}

public class McpOAuthHandlerTests
{
    [Fact]
    public async Task GetTokenAsync_returns_null_for_unknown_server()
    {
        var handler = new McpOAuthHandler();

        var token = await handler.GetTokenAsync("unknown");

        Assert.Null(token);
    }

    [Fact]
    public async Task StoreAndRetrieve_token()
    {
        var handler = new McpOAuthHandler();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        await handler.StoreTokenAsync("server1", "access_token_123", "refresh_token_456", expiresAt);
        var token = await handler.GetTokenAsync("server1");

        Assert.Equal("access_token_123", token);
    }

    [Fact]
    public async Task GetTokenAsync_returns_null_for_expired_token()
    {
        var handler = new McpOAuthHandler();
        var expiresAt = DateTime.UtcNow.AddSeconds(-1); // Already expired

        await handler.StoreTokenAsync("server1", "old_token", "refresh", expiresAt);
        var token = await handler.GetTokenAsync("server1");

        Assert.Null(token);
    }

    [Fact]
    public async Task RevokeToken_removes_stored_token()
    {
        var handler = new McpOAuthHandler();
        await handler.StoreTokenAsync("server1", "token", "refresh", DateTime.UtcNow.AddHours(1));

        await handler.RevokeTokenAsync("server1");
        var token = await handler.GetTokenAsync("server1");

        Assert.Null(token);
    }

    [Fact]
    public async Task HasValidToken_true_for_fresh_token()
    {
        var handler = new McpOAuthHandler();
        await handler.StoreTokenAsync("server1", "token", "refresh", DateTime.UtcNow.AddHours(1));

        Assert.True(handler.HasValidToken("server1"));
    }

    [Fact]
    public async Task HasValidToken_false_for_expired_token()
    {
        var handler = new McpOAuthHandler();
        await handler.StoreTokenAsync("server1", "token", "refresh", DateTime.UtcNow.AddSeconds(-1));

        Assert.False(handler.HasValidToken("server1"));
    }

    [Fact]
    public async Task GetRefreshTokenAsync_returns_refresh_token()
    {
        var handler = new McpOAuthHandler();
        await handler.StoreTokenAsync("server1", "access", "my_refresh_token", DateTime.UtcNow.AddHours(1));

        var refresh = await handler.GetRefreshTokenAsync("server1");

        Assert.Equal("my_refresh_token", refresh);
    }
}

public class McpHttpClientTests
{
    [Fact]
    public void ServerName_set_from_constructor()
    {
        var client = new McpHttpClient("test-server", "http://localhost:8080");

        Assert.Equal("test-server", client.ServerName);
    }

    [Fact]
    public void IsConnected_false_before_connect()
    {
        var client = new McpHttpClient("test", "http://localhost:8080");

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ListToolsAsync_throws_when_not_connected()
    {
        var client = new McpHttpClient("test", "http://localhost:8080");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListToolsAsync());
    }

    [Fact]
    public async Task CallToolAsync_throws_when_not_connected()
    {
        var client = new McpHttpClient("test", "http://localhost:8080");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallToolAsync("tool", "{}"));
    }
}

public class McpStdioClientTests
{
    [Fact]
    public void ServerName_set_from_constructor()
    {
        var client = new McpStdioClient("test-server", "echo");

        Assert.Equal("test-server", client.ServerName);
    }

    [Fact]
    public void IsConnected_false_before_connect()
    {
        var client = new McpStdioClient("test", "echo");

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ListToolsAsync_throws_when_not_connected()
    {
        var client = new McpStdioClient("test", "echo");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListToolsAsync());
    }

    [Fact]
    public async Task CallToolAsync_throws_when_not_connected()
    {
        var client = new McpStdioClient("test", "echo");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallToolAsync("tool", "{}"));
    }

    [Fact]
    public async Task ConnectAsync_throws_for_invalid_command()
    {
        var client = new McpStdioClient("test", "nonexistent_binary_99999");

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());
    }
}
