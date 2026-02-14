using Microsoft.Extensions.Configuration;
using MyPalClara.Core.Configuration;

namespace MyPalClara.Core.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Bind_CreatesConfigWithDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        var result = ConfigLoader.Bind(config);

        Assert.NotNull(result);
        Assert.Equal("demo-user", result.UserId);
        Assert.NotNull(result.Llm);
        Assert.NotNull(result.Memory);
        Assert.NotNull(result.Database);
        Assert.NotNull(result.Gateway);
        Assert.NotNull(result.Skills);
        Assert.NotNull(result.Telegram);
        Assert.NotNull(result.Slack);
        Assert.NotNull(result.Browser);
    }

    [Fact]
    public void Bind_ReadsEnvironmentVariables()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UserId"] = "test-user",
                ["Llm:Provider"] = "openai",
            })
            .Build();

        var result = ConfigLoader.Bind(config);

        Assert.Equal("test-user", result.UserId);
        Assert.Equal("openai", result.Llm.Provider);
    }

    [Fact]
    public void Bind_NormalizesPostgresUri()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Url"] = "postgres://user:pass@localhost:5432/mydb",
            })
            .Build();

        var result = ConfigLoader.Bind(config);

        Assert.Contains("Host=localhost", result.Database.Url);
        Assert.Contains("Port=5432", result.Database.Url);
        Assert.Contains("Database=mydb", result.Database.Url);
        Assert.Contains("Username=user", result.Database.Url);
        Assert.Contains("Password=pass", result.Database.Url);
    }

    [Fact]
    public void Bind_NormalizesRedisUri()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Memory:RedisUrl"] = "redis://:mypassword@localhost:6379",
            })
            .Build();

        var result = ConfigLoader.Bind(config);

        Assert.Contains("localhost:6379", result.Memory.RedisUrl);
        Assert.Contains("password=mypassword", result.Memory.RedisUrl);
    }

    [Fact]
    public void ClaraConfig_HasDefaultSkillsSettings()
    {
        var config = new ClaraConfig();

        Assert.True(config.Skills.Enabled);
        Assert.Equal("~/.mypalclara/skills", config.Skills.Directory);
    }

    [Fact]
    public void ClaraConfig_HasDefaultTelegramSettings()
    {
        var config = new ClaraConfig();

        Assert.Null(config.Telegram.Token);
        Assert.Empty(config.Telegram.AllowedChatIds);
        Assert.Equal(4096, config.Telegram.MaxMessageLength);
    }
}
