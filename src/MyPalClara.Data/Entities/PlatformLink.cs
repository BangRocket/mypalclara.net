namespace MyPalClara.Data.Entities;

public class PlatformLink
{
    public string Id { get; set; } = null!;
    public string CanonicalUserId { get; set; } = null!;
    public string Platform { get; set; } = null!;
    public string PlatformUserId { get; set; } = null!;
    public string PrefixedUserId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public DateTime? LinkedAt { get; set; }
    public string? LinkedVia { get; set; }

    public CanonicalUser CanonicalUser { get; set; } = null!;
}
