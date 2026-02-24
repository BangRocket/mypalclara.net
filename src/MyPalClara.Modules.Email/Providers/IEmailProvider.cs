using MyPalClara.Modules.Email.Models;

namespace MyPalClara.Modules.Email.Providers;

public interface IEmailProvider
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EmailMessage>> FetchUnreadAsync(string? sinceUid = null, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
