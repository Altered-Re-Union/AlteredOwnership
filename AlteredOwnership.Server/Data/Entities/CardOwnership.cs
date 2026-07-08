namespace AlteredOwnership.Server.Data.Entities;

public class CardOwnership
{
    public Guid UserId { get; set; }

    public string CardReference { get; set; } = default!;

    public int Quantity { get; set; }

    public bool IsUnique { get; set; }
    
    public string? AcquiredFrom { get; set; }
}
