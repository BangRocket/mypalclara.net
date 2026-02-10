using Clara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Personality;

/// <summary>Loads personality system prompt from .md file or inline config.</summary>
public sealed class PersonalityLoader
{
    private readonly ClaraConfig _config;
    private readonly ILogger<PersonalityLoader> _logger;
    private string? _cached;

    public PersonalityLoader(ClaraConfig config, ILogger<PersonalityLoader> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>Returns the personality system prompt text.</summary>
    public string GetPersonality()
    {
        if (_cached is not null) return _cached;

        // Try file first, then inline
        if (!string.IsNullOrEmpty(_config.Bot.PersonalityFile) && File.Exists(_config.Bot.PersonalityFile))
        {
            _cached = File.ReadAllText(_config.Bot.PersonalityFile).Trim();
            _logger.LogInformation("Loaded personality from {File} ({Len} chars)",
                _config.Bot.PersonalityFile, _cached.Length);
        }
        else if (!string.IsNullOrEmpty(_config.Bot.Personality))
        {
            _cached = _config.Bot.Personality.Trim();
            _logger.LogInformation("Using inline personality ({Len} chars)", _cached.Length);
        }
        else
        {
            _cached = "You are Clara, a helpful AI assistant.";
            _logger.LogWarning("No personality configured, using default");
        }

        return _cached;
    }
}
