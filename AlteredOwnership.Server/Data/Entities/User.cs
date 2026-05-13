namespace AlteredOwnership.Server.Data.Entities;

public class User
{
    public Guid Id { get; set; }

    public string KeycloakId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}
