using SteamAuth;
using SteamGuardDesktop;

const string secret = "AAECAwQFBgcICQoLDA0ODw==";
long[] times = [0, 1, 29, 30, 1_700_000_000, 2_000_000_000];
var reference = new SteamGuardAccount { SharedSecret = secret };

foreach (long time in times)
{
    string expected = reference.GenerateSteamGuardCodeForTime(time);
    string actual = SteamGuardCodeGenerator.Generate(secret, time);
    Assert(expected == actual, $"Code mismatch at {time}: {expected} != {actual}");
}

string root = Path.Combine(Path.GetTempPath(), "SteamGuardDesktop.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var account = new SteamGuardAccount
    {
        AccountName = "test-account",
        SharedSecret = secret,
        IdentitySecret = "AQIDBA==",
        RevocationCode = "R12345",
        FullyEnrolled = true
    };

    string vaultPath = Path.Combine(root, "vault.dat");
    var vault = new VaultStore(vaultPath);
    vault.Save([account]);
    Assert(!File.ReadAllText(vaultPath).Contains("test-account", StringComparison.Ordinal), "Vault leaked plaintext.");
    Assert(vault.Load().Single().AccountName == "test-account", "Vault round-trip failed.");

    string backupPath = Path.Combine(root, "account.sgbackup");
    BackupService.Export(backupPath, account, "correct horse battery staple");
    Assert(BackupService.Import(backupPath, "correct horse battery staple").SharedSecret == secret,
        "Backup round-trip failed.");

    bool rejectedWrongPassword = false;
    try
    {
        _ = BackupService.Import(backupPath, "incorrect horse battery staple");
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
        rejectedWrongPassword = true;
    }
    Assert(rejectedWrongPassword, "Backup accepted the wrong password.");
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("All SteamGuardDesktop tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
