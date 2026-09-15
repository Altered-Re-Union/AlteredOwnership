namespace AlteredOwnership.Server.Data.Entities;

public class User
{
    public Guid Id { get; set; }

    public string KeycloakId { get; set; } = default!;

    public UserRole Role { get; set; } = UserRole.Player;

    public AltArtPreferenceMode AltArtPreferenceMode { get; set; } = AltArtPreferenceMode.PerDeck;

    public DateTimeOffset CreatedAt { get; set; }
}
