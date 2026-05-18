using AlteredOwnership.Server.Infrastructure.Crypto;

namespace AlteredOwnership.Server.Tests.Infrastructure;

public class SecretBoxFileTests
{
    private const string SampleKeyHex = "";
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
