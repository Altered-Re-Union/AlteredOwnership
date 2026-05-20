namespace AlteredOwnership.Server.Infrastructure.Crypto;

public class EquinoxImportOptions
{
    public const string SectionName = "EquinoxImport";

    // Hex-encoded 32-byte libsodium secretbox key, shared with Equinox, used to
    // decrypt the encrypted/collection.csv.enc entry inside an uploaded export.
    public string DecryptionKeyHex { get; set; } = "";
}
