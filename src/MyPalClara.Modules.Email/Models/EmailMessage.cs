namespace MyPalClara.Modules.Email.Models;

public record EmailMessage(string Uid, string From, string Subject, string Body, DateTime ReceivedAt,
    bool HasAttachment = false);
