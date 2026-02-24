using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

/// <summary>
/// Registers all built-in core tools with the tool registry.
/// Called once during gateway startup after DI is fully configured.
/// </summary>
public static class CoreToolsRegistrar
{
    /// <summary>
    /// Registers all 7 core tool groups (24 tools total) with the given registry.
    /// </summary>
    public static void RegisterAll(
        IToolRegistry registry,
        IServiceScopeFactory scopeFactory,
        IGatewayBridge bridge)
    {
        // Terminal tools (2): execute_command, get_command_history
        TerminalTools.Register(registry);

        // File storage tools (4): save_to_local, list_local_files, read_local_file, delete_local_file
        FileStorageTools.Register(registry);

        // Process manager tools (5): process_start, process_status, process_output, process_stop, process_list
        var processManager = new ProcessManagerService();
        ProcessManagerTools.Register(registry, processManager);

        // Chat history tools (2): search_chat_history, get_chat_history
        ChatHistoryTools.Register(registry, scopeFactory);

        // System log tools (3): search_logs, get_recent_logs, get_error_logs
        SystemLogTools.Register(registry, scopeFactory);

        // Personality tools (1): update_personality
        PersonalityTools.Register(registry, scopeFactory);

        // Discord tools (7): send_discord_file, format_discord_message, add_discord_reaction,
        //   send_discord_embed, create_discord_thread, edit_discord_message, send_discord_buttons
        DiscordTools.Register(registry, bridge);
    }
}
