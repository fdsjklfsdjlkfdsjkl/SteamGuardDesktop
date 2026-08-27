using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace SteamGuardDesktop;

internal sealed class SessionLoginForm : Form
{
    private readonly SteamGuardAccount _account;
    private readonly Action _saveAccount;
    private readonly TextBox _password = new() { Width = 300, UseSystemPasswordChar = true };
    private readonly Label _status = new() { AutoSize = false, Width = 430, Height = 55 };
    private readonly Button _signIn = new() { Text = "Sign in", Width = 120 };

    public bool LoginSucceeded { get; private set; }

    public SessionLoginForm(SteamGuardAccount account, Action saveAccount)
    {
        _account = account;
        _saveAccount = saveAccount;
        Text = $"Sign in — {account.AccountName}";
        Width = 500;
        Height = 245;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 4
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Steam account", AutoSize = true }, 0, 0);
        table.Controls.Add(new Label { Text = account.AccountName, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 1, 0);
        table.Controls.Add(new Label { Text = "Steam password", AutoSize = true }, 0, 1);
        table.Controls.Add(_password, 1, 1);
        table.Controls.Add(_signIn, 1, 2);
        table.Controls.Add(_status, 0, 3);
        table.SetColumnSpan(_status, 2);
        Controls.Add(table);

        _status.Text = "This refreshes the encrypted Steam session used to load and respond to trade and Market confirmations. The password is never saved.";
        _signIn.Click += async (_, _) => await SignInAsync();
        AcceptButton = _signIn;
    }

    private async Task SignInAsync()
    {
        if (string.IsNullOrEmpty(_password.Text))
        {
            MessageBox.Show(this, "Enter your Steam password.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _signIn.Enabled = false;
        UseWaitCursor = true;
        _status.Text = "Connecting to Steam...";
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
                    Username = _account.AccountName,
                    Password = _password.Text,
                    IsPersistentSession = true,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = new StoredAccountAuthenticator(_account, this)
                });

            AuthPollResult response = await authSession.PollingWaitForResultAsync();
            if (_account.Session is not null && _account.Session.SteamID != 0 &&
                _account.Session.SteamID != authSession.SteamID.ConvertToUInt64())
                throw new InvalidOperationException("Steam returned a different account than the selected authenticator.");

            _account.Session = new SessionData
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = response.AccessToken,
                RefreshToken = response.RefreshToken
            };
            _saveAccount();
            _password.Clear();
            LoginSucceeded = true;
            MessageBox.Show(this, "Steam session refreshed. Trade and Market confirmations are now available.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            ShowFailure("Timed out while connecting to Steam.");
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message);
        }
        finally
        {
            _password.Clear();
            client?.Disconnect();
            _signIn.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void ShowFailure(string message)
    {
        _status.Text = "Sign-in failed: " + message;
        MessageBox.Show(this, _status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
