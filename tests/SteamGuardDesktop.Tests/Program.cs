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

byte[] loginSignature = LoginApprovalSignature.Build(
    "zvIayp3JPvtvX/QGHqsqKBk/44s=", 76561197960265728, 1, 2372462679780599330);
byte[] expectedLoginSignature =
[
    56, 233, 253, 249, 254, 89, 110, 161, 18, 35, 35, 144, 14, 217, 210, 150,
    170, 110, 61, 166, 176, 161, 140, 211, 108, 78, 138, 202, 61, 52, 85, 46
];
Assert(loginSignature.SequenceEqual(expectedLoginSignature), "Login-approval signature mismatch.");

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
