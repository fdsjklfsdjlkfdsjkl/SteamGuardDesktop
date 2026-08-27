using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SteamAuth;

namespace SteamGuardDesktop;

public sealed class VaultStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamGuardDesktop.v1.local-vault");
    private readonly string _path;

    public VaultStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamGuardDesktop",
            "vault.dat");
    }

    public string PathOnDisk => _path;

    public List<SteamGuardAccount> Load()
    {
        if (!File.Exists(_path))
            return [];

        byte[] protectedBytes = File.ReadAllBytes(_path);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonConvert.DeserializeObject<List<SteamGuardAccount>>(Encoding.UTF8.GetString(plaintext)) ?? [];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(IReadOnlyCollection<SteamGuardAccount> accounts)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory is null)
            throw new InvalidOperationException("Vault path has no parent directory.");

        Directory.CreateDirectory(directory);
        byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(accounts));
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            string temporaryPath = _path + ".tmp";
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
