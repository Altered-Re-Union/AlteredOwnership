namespace AlteredOwnership.Server.Data.Entities;

public class User
{
    public Guid Id { get; set; }

    public string KeycloakId { get; set; } = default!;

    public UserRole Role { get; set; } = UserRole.Player;

    public DateTimeOffset CreatedAt { get; set; }
}
