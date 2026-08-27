# SteamGuardDesktop

A modern, unofficial Steam Guard authenticator for Windows.

SteamGuardDesktop can enroll a Steam Guard authenticator, generate five-character login codes, and approve or deny Steam trade and Market confirmations without relying on the no-longer-supported Steam Desktop Authenticator client.

> [!IMPORTANT]
> This project is not affiliated with Valve or Steam. Valve's official Steam Mobile app provides stronger two-factor separation and should be preferred whenever possible.

> [!WARNING]
> Never download builds from random mirrors. A malicious authenticator can steal your Steam account, inventory, and session. Only use releases from the repository you trust or compile the source yourself.

## Features

- Enroll a new Steam Guard authenticator
- Detect whether Steam sends the enrollment code by email or SMS
- Resume an enrollment that was saved before finalization
- Generate Steam's rotating five-character login codes
- List, approve, and deny trade and Steam Market confirmations
- Import compatible SDA `.maFile` account files
- Encrypt the local vault with Windows DPAPI
- Export and import password-encrypted portable backups
- Display the authenticator recovery/revocation code on demand
- Support multiple Steam accounts
- Run as a self-contained Windows executable

## Security notice

A desktop authenticator keeps the Steam client and its second-factor secrets on the same computer. If malware can run as your Windows user, it may be able to access the program, generated codes, clipboard, or authenticated Steam session.

SteamGuardDesktop reduces accidental exposure, but it cannot make a compromised computer safe:

- Your Steam password is used only during interactive sign-in and is never saved.
- Authenticator secrets and Steam session tokens are stored in a DPAPI-encrypted vault tied to the current Windows user.
- Portable `.sgbackup` files are encrypted with AES-256-GCM and a password-derived key.
- Recovery codes are not included in screenshots or logs by the application.
- Copying a login code places it on the Windows clipboard, where other desktop applications may read it.

Always keep the `R#####` recovery code and an encrypted backup somewhere separate from the computer.

## Download

Download the newest `SteamGuardDesktop-win-x64.zip` from this repository's **Releases** section, extract it, and run `SteamGuardDesktop.exe`.

The self-contained release includes the required .NET runtime. Windows may show an unknown-publisher warning because community builds are not code-signed.

Supported platform: 64-bit Windows 10 or newer.

## Set up a new account

Before beginning, add and verify a phone number in Steam Account Details if Steam requires one for your account.

1. Run `SteamGuardDesktop.exe`.
2. Click **Add account**.
3. Enter your Steam username and password.
4. Complete the email-code or existing-authenticator sign-in challenge shown by Steam.
5. Write down the displayed `R#####` recovery code and store it offline.
6. Retype the recovery code in the application.
7. Enter the new enrollment code Steam sends by email or SMS, as indicated in the window.
8. Click **Finalize** and wait for the success message.
9. Create an **Encrypted backup** and store it away from the computer.

The pending authenticator secret is encrypted and saved before the final enrollment code is submitted. This is intentional: if the application closes or the network fails during finalization, preserving the secret helps avoid an account lockout. Select the pending account and click **Resume setup** to continue.

Do not repeatedly start a new enrollment for the same account.

## Login codes

Select an account in the main window to display its current five-character Steam Guard code. A new code is generated every 30 seconds.

To verify a new setup safely:

1. Keep an existing trusted Steam session signed in.
2. Open Steam in a private browser window.
3. Sign in with your account name and password.
4. Choose to enter a Steam Guard code.
5. Enter the current five-character code from SteamGuardDesktop.

Do not sign out of all trusted Steam sessions until this test succeeds.

## Trade and Market confirmations

Select an enrolled account and click **Confirm trades**. The confirmation window can:

- Refresh the list of pending Steam actions
- Display the action type, headline, and summary
- Approve selected actions
- Deny selected actions

The application shows a final review prompt before sending an approval or denial to Steam. Read every entry carefully—approving an unexpected trade can permanently transfer inventory items.

Confirmation management requires a current saved Steam session. The application can refresh an access token while its saved refresh token remains valid. If Steam rejects the entire session, sign-in/session-refresh support may be required in a future release.

## Import from SDA

Use **Import** and select an unencrypted `.maFile` exported from Steam Desktop Authenticator. A complete file should include:

- The shared secret used for login codes
- The identity secret used for confirmations
- The Steam account and device identifiers
- Session information for confirmation access

After importing, create a new encrypted `.sgbackup`. Never commit or upload the original `.maFile`.

## Local storage

The live encrypted vault is stored outside the source-code directory:

```text
%LOCALAPPDATA%\SteamGuardDesktop\vault.dat
```

On a normal Windows installation this resolves to a path similar to:

```text
C:\Users\YourName\AppData\Local\SteamGuardDesktop\vault.dat
```

The vault is encrypted for the Windows account that created it. Copying `vault.dat` to another computer or Windows account is not a reliable backup. Use the application's password-encrypted backup feature instead.

## Build from source

Requirements:

- Windows 10 or newer
- .NET 8 SDK
- Git

Clone the repository and build the application:

```powershell
git clone <your-repository-url>
cd SteamGuardDesktop
dotnet build .\src\SteamGuardDesktop\SteamGuardDesktop.csproj -c Release
dotnet run --project .\src\SteamGuardDesktop\SteamGuardDesktop.csproj -c Release
```

Run the local verification tests:

```powershell
dotnet run --project .\tests\SteamGuardDesktop.Tests\SteamGuardDesktop.Tests.csproj -c Release
```

Create a self-contained single-file Windows build:

```powershell
dotnet publish .\src\SteamGuardDesktop\SteamGuardDesktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\dist\win-x64
```

## Troubleshooting

### I did not receive the enrollment code

Read the delivery method shown in the enrollment window. Steam may send the new enrollment code to the account email instead of by SMS. Check the linked email address and spam folder. Do not confuse this with the earlier sign-in verification code.

If the app was closed after showing the recovery code, reopen it, select the pending account, and click **Resume setup**.

### Generated login codes are rejected

Enable automatic date, time, and time-zone synchronization in Windows, then wait for the next 30-second code. Make sure the correct account is selected.

### Confirmations do not load

The saved Steam session may be incomplete or expired. Enrollment performed by this app stores the required session data. An imported `.maFile` must contain current session information as well as the identity secret.

### I lost the local vault

Restore an encrypted `.sgbackup`. If no backup exists, use the saved `R#####` recovery code through Steam's account-recovery flow. If both are unavailable, contact Steam Support and be prepared to prove ownership.

## Project status

This is community software that depends on unofficially documented Steam behavior. Steam can change authentication or confirmation endpoints without notice. Treat each release as experimental, keep recoverable backups, and report reproducible issues without attaching secrets, account files, or full authentication logs.

## Credits and third-party software

- [SteamAuth](https://github.com/geel9/SteamAuth) is vendored under its MIT license.
- [SteamKit2](https://github.com/SteamRE/SteamKit) is consumed from NuGet under LGPL-2.1-only.
- The original [Steam Desktop Authenticator](https://github.com/Jessecar96/SteamDesktopAuthenticator) inspired the desktop workflow and `.maFile` compatibility.

Steam, Steam Guard, and related names are trademarks of Valve Corporation. This project is not endorsed by or affiliated with Valve.
