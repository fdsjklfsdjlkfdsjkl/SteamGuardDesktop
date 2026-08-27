using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SteamAuth;

namespace SteamGuardDesktop;

public static class BackupService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SGDB1");
    private const int Iterations = 600_000;

    public static void Export(string path, SteamGuardAccount account, string password)
    {
        ValidatePassword(password);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(account));
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Magic);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(Magic);
            stream.Write(salt);
            stream.Write(nonce);
            stream.Write(tag);
            stream.Write(ciphertext);
            stream.Flush(true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static SteamGuardAccount Import(string path, string password)
    {
        byte[] file = File.ReadAllBytes(path);
        int headerSize = Magic.Length + 16 + 12 + 16;
        if (file.Length <= headerSize || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a SteamGuardDesktop backup.");

        ReadOnlySpan<byte> salt = file.AsSpan(Magic.Length, 16);
        ReadOnlySpan<byte> nonce = file.AsSpan(Magic.Length + 16, 12);
        ReadOnlySpan<byte> tag = file.AsSpan(Magic.Length + 28, 16);
        ReadOnlySpan<byte> ciphertext = file.AsSpan(headerSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Magic);
            return JsonConvert.DeserializeObject<SteamGuardAccount>(Encoding.UTF8.GetString(plaintext))
                   ?? throw new InvalidDataException("The backup contains no account.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12)
            throw new ArgumentException("Backup passwords must contain at least 12 characters.", nameof(password));
    }
}
