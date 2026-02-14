using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MyPalClara.App.Windows;

public partial class MainWindow : Window
{
    private GatewayClient? _gateway;
    private ClaraConfig _config = null!;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        _config = ConfigLoader.Bind(configuration);

        var gatewayUri = new Uri($"ws://{_config.Gateway.Host}:{_config.Gateway.Port}/ws");
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
            .CreateLogger<GatewayClient>();
        _gateway = new GatewayClient(gatewayUri, _config.Gateway.Secret, "windows", "windows-app", logger);

        try
        {
            StatusText.Text = " — Connecting...";
            await _gateway.ConnectAsync();
            StatusText.Text = " — Connected";
            StatusBar.Text = $"Connected to {_config.Gateway.Host}:{_config.Gateway.Port} | User: {_config.UserId}";
        }
        catch (Exception ex)
        {
            StatusText.Text = " — Disconnected";
            StatusBar.Text = $"Connection failed: {ex.Message}";
            AddSystemMessage($"Failed to connect to Gateway: {ex.Message}");
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_gateway is not null)
            await _gateway.DisposeAsync();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(InputBox.Text))
        {
            _ = SendMessageAsync();
            e.Handled = true;
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(InputBox.Text))
            await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (_gateway is null || !_gateway.IsConnected)
        {
            AddSystemMessage("Not connected to Gateway.");
            return;
        }

        var input = InputBox.Text.Trim();
        InputBox.Text = "";
        InputBox.Focus();

        AddUserMessage(input);

        var request = new ChatRequest(
            ChannelId: "windows",
            ChannelName: "Windows App",
            ChannelType: "dm",
            UserId: _config.UserId,
            DisplayName: "User",
            Content: input);

        var responseText = "";
        SendButton.IsEnabled = false;

        try
        {
            await foreach (var response in _gateway.ChatAsync(request))
            {
                switch (response)
                {
                    case TextChunk chunk:
                        responseText += chunk.Text;
                        break;
                    case Complete complete:
                        responseText = complete.FullText;
                        break;
                    case ErrorMessage error:
                        responseText = $"Error: {error.Message}";
                        break;
                }
            }

            if (!string.IsNullOrEmpty(responseText))
                AddAssistantMessage(responseText);
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Error: {ex.Message}");
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private void AddUserMessage(string text)
    {
        var border = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16213e")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(60, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
            }
        };
        ChatPanel.Children.Add(border);
        ChatScroller.ScrollToBottom();
    }

    private void AddAssistantMessage(string text)
    {
        var border = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3460")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(12, 4, 60, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
            }
        };
        ChatPanel.Children.Add(border);
        ChatScroller.ScrollToBottom();
    }

    private void AddSystemMessage(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        ChatPanel.Children.Add(tb);
        ChatScroller.ScrollToBottom();
    }
}
