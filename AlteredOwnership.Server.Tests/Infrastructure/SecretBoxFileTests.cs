using AlteredOwnership.Server.Infrastructure.Crypto;

namespace AlteredOwnership.Server.Tests.Infrastructure;

public class SecretBoxFileTests
{
    // A key generated solely to encrypt this test fixture — not the real Equinox
    // shared key (which is never committed; see EquinoxImportOptions.DecryptionKeyHex).
    private const string SampleKeyHex = "BEF7D79EF3E726C3D42F8EF39AF8E5764DF3C73BFCFED2B06B4AC9AFD16C01F0";
    private const string PlaintextFile = "Encrypted_collection_test.csv";
    private const string EncryptedFile = "Encrypted_collection_test.csv.enc";

    [Fact]
    public void DecryptFile_returns_original_csv_bytes()
    {
        var key = Convert.FromHexString(SampleKeyHex);
        var expected = File.ReadAllBytes(FixturePath(PlaintextFile));

        var actual = SecretBoxFile.DecryptFile(FixturePath(EncryptedFile), key);

        Assert.Equal(expected, actual);
    }

    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Infrastructure", fileName);
}
