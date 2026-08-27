using System.Security.Cryptography;
using System.Text;

namespace SteamGuardDesktop;

public static class SteamGuardCodeGenerator
{
    private static readonly byte[] Alphabet = Encoding.ASCII.GetBytes("23456789BCDFGHJKMNPQRTVWXY");

    public static string Generate(string sharedSecretBase64, long unixTimeSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedSecretBase64);

        byte[] secret = Convert.FromBase64String(sharedSecretBase64);
        long timeSlice = unixTimeSeconds / 30;
        Span<byte> timeBytes = stackalloc byte[8];
        for (int i = 7; i >= 0; i--)
        {
            timeBytes[i] = (byte)timeSlice;
            timeSlice >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        byte[] hash = hmac.ComputeHash(timeBytes.ToArray());
        int offset = hash[^1] & 0x0f;
        int value = ((hash[offset] & 0x7f) << 24)
                  | ((hash[offset + 1] & 0xff) << 16)
                  | ((hash[offset + 2] & 0xff) << 8)
                  | (hash[offset + 3] & 0xff);

        Span<byte> result = stackalloc byte[5];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Alphabet[value % Alphabet.Length];
            value /= Alphabet.Length;
        }

        CryptographicOperations.ZeroMemory(secret);
        return Encoding.ASCII.GetString(result);
    }

    public static int SecondsRemaining(long unixTimeSeconds) => 30 - (int)(unixTimeSeconds % 30);
}
