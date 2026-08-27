using Newtonsoft.Json;
using SteamAuth;

namespace SteamGuardDesktop;

internal sealed class MainForm : Form
{
    private readonly VaultStore _vault = new();
    private readonly ListBox _accounts = new() { Dock = DockStyle.Fill };
    private readonly Label _accountName = new() { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
    private readonly Label _code = new() { AutoSize = true, Font = new Font("Consolas", 34, FontStyle.Bold), Text = "-----" };
    private readonly ProgressBar _countdown = new() { Minimum = 0, Maximum = 30, Width = 310 };
    private readonly Label _seconds = new() { AutoSize = true };
    private readonly Label _state = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private List<SteamGuardAccount> _items = [];

    public MainForm()
    {
        Text = "Steam Guard Desktop";
        Width = 790;
        Height = 520;
        MinimumSize = new Size(720, 450);

        var warning = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(255, 244, 204),
            Text = "Unofficial desktop authenticator. A compromised Windows account can access your codes. Keep an offline recovery code and encrypted backup."
        };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var accountPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        accountPanel.Controls.Add(_accounts);
        body.Controls.Add(accountPanel, 0, 0);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(28)
        };
        content.Controls.Add(_accountName);
        content.Controls.Add(_code);
        content.Controls.Add(_countdown);
        content.Controls.Add(_seconds);
        content.Controls.Add(_state);

        var copy = new Button { Text = "Copy code", Width = 125, Height = 34 };
        var signIn = new Button { Text = "Sign in / refresh", Width = 135, Height = 34 };
        var loginApprovals = new Button { Text = "Login approvals", Width = 135, Height = 34 };
        var confirmations = new Button { Text = "Trades & listings", Width = 145, Height = 34 };
        var recovery = new Button { Text = "Show recovery code", Width = 155, Height = 34 };
        var add = new Button { Text = "Add account", Width = 125, Height = 34 };
        var resume = new Button { Text = "Resume setup", Width = 125, Height = 34 };
        var import = new Button { Text = "Import", Width = 125, Height = 34 };
        var export = new Button { Text = "Encrypted backup", Width = 145, Height = 34 };
        var remove = new Button { Text = "Remove local copy", Width = 145, Height = 34 };
        var buttons = new FlowLayoutPanel { AutoSize = true, Width = 480, Margin = new Padding(0, 24, 0, 0) };
        buttons.Controls.AddRange([copy, signIn, loginApprovals, confirmations, recovery, add, resume, import, export, remove]);
        content.Controls.Add(buttons);
        body.Controls.Add(content, 1, 0);
        Controls.Add(body);
        Controls.Add(warning);

        _accounts.SelectedIndexChanged += (_, _) => RenderCode();
        _timer.Tick += (_, _) => RenderCode();
        copy.Click += (_, _) => CopyCode();
        signIn.Click += (_, _) => SignInSelectedAccount();
        loginApprovals.Click += (_, _) => ShowLoginApprovals();
        confirmations.Click += (_, _) => ShowConfirmations();
        recovery.Click += (_, _) => ShowRecoveryCode();
        add.Click += (_, _) => AddAccount();
        resume.Click += (_, _) => ResumeEnrollment();
        import.Click += (_, _) => ImportAccount();
        export.Click += (_, _) => ExportAccount();
        remove.Click += (_, _) => RemoveLocalCopy();

        LoadVault();
        _timer.Start();
    }

    private SteamGuardAccount? Selected => _accounts.SelectedIndex >= 0 && _accounts.SelectedIndex < _items.Count
        ? _items[_accounts.SelectedIndex]
        : null;

    private void LoadVault()
    {
        try
        {
            _items = _vault.Load().OrderBy(x => x.AccountName, StringComparer.OrdinalIgnoreCase).ToList();
            _accounts.Items.Clear();
            _accounts.Items.AddRange(_items.Select(x => x.AccountName ?? "Unknown account").ToArray());
            if (_items.Count > 0)
                _accounts.SelectedIndex = 0;
            else
                RenderCode();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The encrypted vault could not be opened: " + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderCode()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
        {
            _accountName.Text = "No accounts yet";
            _code.Text = "-----";
            _seconds.Text = "Use Add account or Import to begin.";
            _state.Text = string.Empty;
            _countdown.Value = 0;
            return;
        }

        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int remaining = SteamGuardCodeGenerator.SecondsRemaining(now);
            _accountName.Text = account.AccountName;
            _code.Text = SteamGuardCodeGenerator.Generate(account.SharedSecret, now);
            _countdown.Value = remaining;
            _seconds.Text = $"New code in {remaining} second{(remaining == 1 ? "" : "s")}";
            _state.Text = account.FullyEnrolled ? "Enrolled" : "Pending enrollment — keep the recovery code";
        }
        catch (Exception ex)
        {
            _code.Text = "ERROR";
            _state.Text = ex.Message;
        }
    }

    private void CopyCode()
    {
        if (Selected is null || _code.Text.Length != 5)
            return;
        Clipboard.SetText(_code.Text);
        _state.Text = "Code copied. Clipboard contents can be read by other desktop apps.";
    }

    private void ShowRecoveryCode()
    {
        if (Selected is null)
            return;
        MessageBox.Show(this, Selected.RevocationCode,
            $"Recovery code — {Selected.AccountName}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowConfirmations()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
            return;
        if (!account.FullyEnrolled)
        {
            MessageBox.Show(this, "Finish this account's authenticator setup before loading confirmations.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (NeedsFreshSession(account) && !SignInAccount(account))
            return;

        using var form = new ConfirmationsForm(account, () => _vault.Save(_items));
        form.ShowDialog(this);
    }

    private void ShowLoginApprovals()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
            return;
        if (!account.FullyEnrolled)
        {
            MessageBox.Show(this, "Finish this account's authenticator setup before loading login approvals.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (NeedsFreshSession(account) && !SignInAccount(account))
            return;

        using var form = new LoginApprovalsForm(account, () => _vault.Save(_items));
        form.ShowDialog(this);
    }

    private void SignInSelectedAccount()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
            return;
        if (!account.FullyEnrolled)
        {
            MessageBox.Show(this, "Finish this account's authenticator setup first.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _ = SignInAccount(account);
    }

    private bool SignInAccount(SteamGuardAccount account)
    {
        using var form = new SessionLoginForm(account, () => _vault.Save(_items));
        form.ShowDialog(this);
        return form.LoginSucceeded;
    }

    private static bool NeedsFreshSession(SteamGuardAccount account)
    {
        try
        {
            return account.Session is null || string.IsNullOrWhiteSpace(account.Session.RefreshToken) ||
                   account.Session.IsRefreshTokenExpired();
        }
        catch
        {
            return true;
        }
    }

    private void AddAccount()
    {
        using var form = new EnrollmentForm(_vault);
        form.ShowDialog(this);
        LoadVault();
    }

    private void ResumeEnrollment()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
            return;
        if (account.FullyEnrolled)
        {
            MessageBox.Show(this, "This account is already enrolled.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var form = new EnrollmentForm(_vault, account);
            form.ShowDialog(this);
            LoadVault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Pending enrollment cannot be resumed: " + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportAccount()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "SteamGuardDesktop backup (*.sgbackup)|*.sgbackup|SDA maFile (*.maFile;*.json)|*.maFile;*.json",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            SteamGuardAccount account;
            if (string.Equals(Path.GetExtension(picker.FileName), ".sgbackup", StringComparison.OrdinalIgnoreCase))
            {
                string? password = PromptDialog.Show(this, "Import backup", "Backup password:", true);
                if (password is null)
                    return;
                account = BackupService.Import(picker.FileName, password);
            }
            else
            {
                account = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(picker.FileName))
                          ?? throw new InvalidDataException("No account was found in that file.");
            }

            ValidateAccount(account);
            _items.RemoveAll(x => string.Equals(x.AccountName, account.AccountName, StringComparison.OrdinalIgnoreCase));
            _items.Add(account);
            _vault.Save(_items);
            LoadVault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed: " + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportAccount()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
            return;
        string? password = PromptDialog.Show(this, "Encrypted backup",
            "Choose a unique backup password (12+ characters):", true);
        if (password is null)
            return;
        string? confirm = PromptDialog.Show(this, "Encrypted backup", "Retype the backup password:", true);
        if (password != confirm)
        {
            MessageBox.Show(this, "Passwords did not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var picker = new SaveFileDialog
        {
            Filter = "SteamGuardDesktop backup (*.sgbackup)|*.sgbackup",
            FileName = (account.AccountName ?? "steam-account") + ".sgbackup"
        };
        if (picker.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            BackupService.Export(picker.FileName, account, password);
            MessageBox.Show(this, "Encrypted backup saved. Store it separately from this PC.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Backup failed: " + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveLocalCopy()
    {
        SteamGuardAccount? account = Selected;
        if (account is null)
            return;
        if (MessageBox.Show(this,
                "This only removes the local encrypted copy. It does NOT disable Steam Guard. Continue only if you have another working authenticator or backup.",
                "Remove local copy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _items.Remove(account);
        _vault.Save(_items);
        LoadVault();
    }

    private static void ValidateAccount(SteamGuardAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.AccountName) || string.IsNullOrWhiteSpace(account.SharedSecret))
            throw new InvalidDataException("The file is missing the account name or shared secret.");
        _ = Convert.FromBase64String(account.SharedSecret);
    }
}
