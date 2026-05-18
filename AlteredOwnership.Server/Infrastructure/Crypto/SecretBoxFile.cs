using Sodium;

namespace AlteredOwnership.Server.Infrastructure.Crypto;

// Mirrors PHP sodium_crypto_secretbox / _open over a file laid out as nonce (24B) || libsodium ciphertext (MAC 16 || data N).
public static class SecretBoxFile
{
    public const int KeySize = 32;
    public const int NonceSize = 24;
    public const int MacSize = 16;

    public static byte[] DecryptFile(string filePath, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Encrypted file not found.", filePath);

        var data = File.ReadAllBytes(filePath);
        if (data.Length < NonceSize + MacSize)
            throw new InvalidDataException("Encrypted file is too short to contain a nonce and an authentication tag.");

        var nonce = data.AsSpan(0, NonceSize).ToArray();
        var ciphertext = data.AsSpan(NonceSize).ToArray();

        return SecretBox.Open(ciphertext, nonce, key);
    }

    public static void EncryptFile(string sourcePath, string destinationPath, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source file not found.", sourcePath);

        var plaintext = File.ReadAllBytes(sourcePath);
        var nonce = SodiumCore.GetRandomBytes(NonceSize);
        var ciphertext = SecretBox.Create(plaintext, nonce, key);

        var output = new byte[NonceSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSize, ciphertext.Length);

        File.WriteAllBytes(destinationPath, output);
    }
}
