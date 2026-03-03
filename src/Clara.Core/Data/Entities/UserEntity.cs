namespace Clara.Core.Data.Entities;

public class UserEntity
{
    public Guid Id { get; set; }
    public string PlatformId { get; set; } = "";
    public string Platform { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? Preferences { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
