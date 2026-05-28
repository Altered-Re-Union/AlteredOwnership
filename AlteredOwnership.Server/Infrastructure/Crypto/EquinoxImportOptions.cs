namespace AlteredOwnership.Server.Infrastructure.Crypto;

public class EquinoxImportOptions
{
    public const string SectionName = "EquinoxImport";

    // Hex-encoded 32-byte libsodium secretbox key, shared with Equinox, used to
    // decrypt the encrypted/collection.csv.enc entry inside an uploaded export.
    public string DecryptionKeyHex { get; set; } = "";

    // Dev-only escape hatch for contributors without the shared key: read the
    // plaintext clear/collection.csv entry instead of decrypting. Refused at
    // startup in Production (see Program.cs) so it can never ship enabled.
    public bool AllowUnencrypted { get; set; }
}
