using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace SteamGuardDesktop;

internal sealed class EnrollmentForm : Form
{
    private readonly VaultStore _vault;
    private readonly TextBox _username = new() { Width = 300 };
    private readonly TextBox _password = new() { Width = 300, UseSystemPasswordChar = true };
    private readonly TextBox _smsCode = new() { Width = 180, Enabled = false };
    private readonly TextBox _recoveryConfirm = new() { Width = 180, Enabled = false };
    private readonly Label _activationLabel = new() { Text = "Activation code", AutoSize = true };
    private readonly Label _status = new() { AutoSize = false, Width = 465, Height = 55 };
    private readonly Label _recovery = new() { AutoSize = false, Width = 465, Height = 35, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly Button _signIn = new() { Text = "Sign in and enroll", Width = 160 };
    private readonly Button _finalize = new() { Text = "Finalize with SMS", Width = 160, Enabled = false };
    private AuthenticatorLinker? _linker;

    public EnrollmentForm(VaultStore vault, SteamGuardAccount? pendingAccount = null)
    {
        _vault = vault;
        Text = "Add Steam account";
        Width = 540;
        Height = 430;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 9,
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Steam username", AutoSize = true }, 0, 0);
        table.Controls.Add(_username, 1, 0);
        table.Controls.Add(new Label { Text = "Steam password", AutoSize = true }, 0, 1);
        table.Controls.Add(_password, 1, 1);
        var passwordNote = new Label
        {
            Text = "Your password is used only for this sign-in and is never written to disk.",
            AutoSize = false,
            Width = 465,
            Height = 38,
            ForeColor = Color.DimGray
        };
        table.Controls.Add(passwordNote, 0, 2);
        table.SetColumnSpan(passwordNote, 2);
        table.Controls.Add(_signIn, 1, 3);
        table.Controls.Add(_status, 0, 4);
        table.SetColumnSpan(_status, 2);
        table.Controls.Add(new Label { Text = "Recovery code", AutoSize = true }, 0, 5);
        table.Controls.Add(_recovery, 1, 5);
        table.Controls.Add(new Label { Text = "Retype recovery code", AutoSize = true }, 0, 6);
        table.Controls.Add(_recoveryConfirm, 1, 6);
        table.Controls.Add(_activationLabel, 0, 7);
        table.Controls.Add(_smsCode, 1, 7);
        table.Controls.Add(_finalize, 1, 8);
        Controls.Add(table);

        _signIn.Click += async (_, _) => await BeginEnrollmentAsync();
        _finalize.Click += async (_, _) => await FinalizeEnrollmentAsync();

        if (pendingAccount is not null)
            LoadPendingEnrollment(pendingAccount);
    }

    private async Task BeginEnrollmentAsync()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrEmpty(_password.Text))
        {
            MessageBox.Show(this, "Enter your Steam username and password.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true, "Connecting to Steam...");
        SteamClient? client = null;
        try
        {
            client = new SteamClient();
            client.Connect();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!client.IsConnected)
                await Task.Delay(250, timeout.Token);

            CredentialsAuthSession authSession = await client.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = _username.Text.Trim(),
                    Password = _password.Text,
                    IsPersistentSession = false,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = new DialogAuthenticator(this)
                });

            AuthPollResult response = await authSession.PollingWaitForResultAsync();
            _password.Clear();

            var session = new SessionData
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = response.AccessToken,
                RefreshToken = response.RefreshToken
            };
            _linker = new AuthenticatorLinker(session);
            AuthenticatorLinker.LinkResult result = await _linker.AddAuthenticator();

            if (result == AuthenticatorLinker.LinkResult.MustProvidePhoneNumber)
                throw new InvalidOperationException("Steam says this account has no verified phone number. Add and verify the number in Steam Account Details, then try again.");
            if (result == AuthenticatorLinker.LinkResult.AuthenticatorPresent)
                throw new InvalidOperationException("This account already has a mobile authenticator. Remove or transfer it before enrolling this one.");
            if (result != AuthenticatorLinker.LinkResult.AwaitingFinalization || _linker.LinkedAccount is null)
                throw new InvalidOperationException($"Steam did not start enrollment: {result}.");

            // Save before finalization. If Steam links the secret but the app closes, this prevents lockout.
            Upsert(_linker.LinkedAccount);
            ConfigureFinalization(_linker.LinkedAccount);
        }
        catch (OperationCanceledException)
        {
            ShowFailure("Timed out while connecting to Steam.");
        }
        catch (Exception ex)
        {
            _password.Clear();
            ShowFailure(ex.Message);
        }
        finally
        {
            client?.Disconnect();
            if (_linker is null)
                SetBusy(false, _status.Text);
        }
    }

    private async Task FinalizeEnrollmentAsync()
    {
        if (_linker?.LinkedAccount is null)
            return;
        if (!string.Equals(_recoveryConfirm.Text.Trim(), _linker.LinkedAccount.RevocationCode,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Retype the recovery code exactly before finalizing.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_smsCode.Text))
        {
            MessageBox.Show(this, $"Enter the {ActivationMethod(_linker.LinkedAccount)} activation code.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _finalize.Enabled = false;
        _status.Text = "Finalizing authenticator with Steam...";
        try
        {
            if (_linker.LinkedAccount.Session?.IsAccessTokenExpired() == true)
            {
                await _linker.LinkedAccount.Session.RefreshAccessToken(true);
                Upsert(_linker.LinkedAccount);
            }

            AuthenticatorLinker.FinalizeResult result = await _linker.FinalizeAddAuthenticator(_smsCode.Text.Trim());
            if (result != AuthenticatorLinker.FinalizeResult.Success)
            {
                _finalize.Enabled = true;
                _status.Text = result == AuthenticatorLinker.FinalizeResult.BadSMSCode
                    ? $"Steam rejected that {ActivationMethod(_linker.LinkedAccount)} code. Check it and try again."
                    : $"Steam could not finalize enrollment: {result}. Keep the recovery code and pending vault entry.";
                return;
            }

            Upsert(_linker.LinkedAccount);
            MessageBox.Show(this,
                "Authenticator enrolled successfully. Keep the recovery code and an encrypted backup somewhere off this PC.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _finalize.Enabled = true;
            _status.Text = "Finalization failed. The pending secret remains encrypted in the vault: " + ex.Message;
        }
    }

    private void Upsert(SteamGuardAccount account)
    {
        List<SteamGuardAccount> accounts = _vault.Load();
        accounts.RemoveAll(existing => string.Equals(existing.AccountName, account.AccountName, StringComparison.OrdinalIgnoreCase));
        accounts.Add(account);
        _vault.Save(accounts);
    }

    private void LoadPendingEnrollment(SteamGuardAccount account)
    {
        if (account.FullyEnrolled)
            throw new InvalidOperationException("This authenticator is already enrolled.");
        if (account.Session is null)
            throw new InvalidOperationException("The pending account has no saved Steam session and cannot be resumed.");

        _linker = new AuthenticatorLinker(account.Session, account);
        _username.Text = account.AccountName;
        ConfigureFinalization(account);
    }

    private void ConfigureFinalization(SteamGuardAccount account)
    {
        string method = ActivationMethod(account);
        _recovery.Text = account.RevocationCode;
        _recoveryConfirm.Enabled = true;
        _smsCode.Enabled = true;
        _finalize.Enabled = true;
        _signIn.Enabled = false;
        _username.Enabled = false;
        _password.Enabled = false;
        _activationLabel.Text = char.ToUpperInvariant(method[0]) + method[1..] + " activation code";
        _status.Text = method == "email"
            ? "Enrollment started and the pending secret was encrypted locally. Steam sent a NEW enrollment code to your account email. Save the recovery code, then enter the emailed code below."
            : $"Enrollment started and the pending secret was encrypted locally. Steam sent a code to the phone ending in {account.PhoneNumberHint}. Save the recovery code, then enter the SMS below.";
    }

    private static string ActivationMethod(SteamGuardAccount account) => account.ConfirmType == 3 ? "email" : "SMS";

    private void SetBusy(bool busy, string status)
    {
        _signIn.Enabled = !busy;
        _status.Text = status;
        UseWaitCursor = busy;
    }

    private void ShowFailure(string message)
    {
        _status.Text = message;
        MessageBox.Show(this, message, "Enrollment failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
