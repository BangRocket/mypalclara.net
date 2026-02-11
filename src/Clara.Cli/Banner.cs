using System.Reflection;
using Spectre.Console;

namespace Clara.Cli;

/// <summary>Startup banner — port of Python's gateway/banner.py.</summary>
internal static class Banner
{
    // Pre-generated braille portrait (compact 22 cols)
    private static readonly string[] Portrait =
    [
        "⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⢀⡀⠀⢤⣿⣿⣿⣿⣿⣿",
        "⠀⠀⠀⠀⠀⣠⣶⣿⡁⠀⠀⠀⣹⠇⠀⠀⠙⣿⣿⣿⣿⣿",
        "⠀⠀⠀⠀⣼⣿⣿⣿⠗⠋⠁⠀⠙⠢⡀⠀⠀⠈⢿⣿⣿⣿",
        "⠀⠀⠀⣾⣿⣿⣿⠇⠀⠀⠀⠀⠀⠀⠘⣄⠀⠀⠈⢿⣿⣿",
        "⠀⢀⣾⣿⣿⣿⣯⣄⡀⠀⢀⣠⡄⠀⠀⢸⣧⡀⣄⠈⢿⣿",
        "⢀⣾⣿⣿⣿⣿⣿⣿⣷⠀⠐⢿⣶⠶⠀⠀⣿⣷⡼⣆⢸⣿",
        "⢸⣿⣿⣿⣿⡟⠛⠉⠃⠀⠀⠀⠀⠀⠀⠀⢸⣿⣿⣿⠈⣿",
        "⢸⣿⣿⣿⣿⣷⣀⡰⣶⡤⠄⠀⠀⠀⠀⢀⣼⡿⠿⠃⠀⣿",
        "⢠⣿⣿⣿⣿⣿⣿⣿⣷⡦⠤⠤⠀⠀⠀⣼⣿⡇⢰⡆⠀⠘",
        "⣿⣿⣿⣿⣿⣿⣿⣿⠛⠋⠀⠀⢀⣠⣾⣿⣿⣧⠘⢇⢀⠀",
        "⣿⣿⣿⣿⣿⣿⣿⣿⣷⣶⣶⠖⠉⠚⣿⣿⣿⣿⣧⣸⣿⣿",
        "⢿⣿⣿⣿⣿⣿⣿⣿⣿⣿⠃⠀⠀⢀⣿⣿⣿⣿⣿⠟⠉⣾",
        "⣾⣿⣿⣿⣿⣿⣿⣿⣿⡟⠀⠀⠀⠀⣻⣿⣯⣿⣡⣿⠉⠚",
    ];

    // Block-letter MYPALCLARA
    private static readonly string[] BannerText =
    [
        "  ███╗   ███╗██╗   ██╗██████╗  █████╗ ██╗      ██████╗ ██╗      █████╗  ██████╗  █████╗ ",
        "  ████╗ ████║╚██╗ ██╔╝██╔══██╗██╔══██╗██║     ██╔════╝ ██║     ██╔══██╗ ██╔══██╗██╔══██╗",
        "  ██╔████╔██║ ╚████╔╝ ██████╔╝███████║██║     ██║      ██║     ███████║ ██████╔╝███████║ ",
        "  ██║╚██╔╝██║  ╚██╔╝  ██╔═══╝ ██╔══██║██║     ██║      ██║     ██╔══██║ ██╔══██╗██╔══██║ ",
        "  ██║ ╚═╝ ██║   ██║   ██║     ██║  ██║███████╗╚██████╗ ███████╗██║  ██║ ██║  ██║██║  ██║ ",
        "  ╚═╝     ╚═╝   ╚═╝   ╚═╝     ╚═╝  ╚═╝╚══════╝ ╚═════╝ ╚══════╝╚═╝  ╚═╝ ╚═╝  ╚═╝╚═╝  ╚═╝",
    ];

    // Gradient: green → warm gold (ANSI 256 palette mapped to hex)
    private static readonly string[] Gradient = ["#87d7af", "#afd787", "#d7d787", "#ffd787", "#ffaf87", "#ff8787"];
    private const string PortraitColor = "#87af87";

    public static string GetVersion()
    {
        return Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "dev";
    }

    public static void Print(IAnsiConsole console)
    {
        var version = GetVersion();
        const int gap = 3;
        var portraitWidth = Portrait.Max(l => l.Length);
        var bannerStart = Math.Max(0, (Portrait.Length - BannerText.Length) / 2);
        var totalHeight = Math.Max(Portrait.Length, bannerStart + BannerText.Length + 3);
        var separator = new string(' ', gap);

        for (var i = 0; i < totalHeight; i++)
        {
            // Portrait part
            string portraitMarkup;
            int pLen;
            if (i < Portrait.Length)
            {
                pLen = Portrait[i].Length;
                portraitMarkup = $"[{PortraitColor}]{Portrait[i].EscapeMarkup()}[/]";
            }
            else
            {
                pLen = 0;
                portraitMarkup = "";
            }

            var padding = new string(' ', portraitWidth - pLen);

            // Banner/text part
            var bannerIdx = i - bannerStart;
            string bannerMarkup;
            if (bannerIdx >= 0 && bannerIdx < BannerText.Length)
            {
                var colorIdx = (int)((double)bannerIdx / Math.Max(BannerText.Length - 1, 1) * (Gradient.Length - 1));
                bannerMarkup = $"[{Gradient[colorIdx]}]{BannerText[bannerIdx].EscapeMarkup()}[/]";
            }
            else if (bannerIdx == BannerText.Length + 1)
            {
                bannerMarkup = $"  [dim]v{version.EscapeMarkup()} \u2022 Your AI companion[/]";
            }
            else
            {
                bannerMarkup = "";
            }

            // Only emit lines with visible content
            if (i < Portrait.Length || !string.IsNullOrEmpty(bannerMarkup))
            {
                console.Markup($"{portraitMarkup}{padding}{separator}{bannerMarkup}");
                console.WriteLine();
            }
        }

        console.WriteLine();
    }
}
