using Sodium;

namespace AlteredOwnership.Server.Infrastructure.Crypto;

// Reads an export payload laid out as nonce (24B) || ciphertext (MAC 16 || data N) and returns its decrypted bytes.
public static class SecretBoxFile
{
    public const int KeySize = 32;
    public const int NonceSize = 24;
    public const int MacSize = 16;

    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        if (data.Length < NonceSize + MacSize)
            throw new InvalidDataException("Encrypted file is too short to contain a nonce and an authentication tag.");

        var nonce = data.AsSpan(0, NonceSize).ToArray();
        var ciphertext = data.AsSpan(NonceSize).ToArray();

        return SecretBox.Open(ciphertext, nonce, key);
    }

    public static byte[] DecryptFile(string filePath, byte[] key)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Encrypted file not found.", filePath);

        return Decrypt(File.ReadAllBytes(filePath), key);
    }
}
