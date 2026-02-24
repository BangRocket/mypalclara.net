namespace MyPalClara.Data.Entities;

public class Message
{
    public int Id { get; set; }
    public string SessionId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public Session Session { get; set; } = null!;
}
