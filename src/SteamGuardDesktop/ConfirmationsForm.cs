using SteamAuth;

namespace SteamGuardDesktop;

internal sealed class ConfirmationsForm : Form
{
    private readonly SteamGuardAccount _account;
    private readonly Action _saveAccount;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        HideSelection = false
    };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(8, 5, 8, 0) };
    private readonly Button _refresh = new() { Text = "Refresh", Width = 100 };
    private readonly Button _approve = new() { Text = "Approve selected", Width = 135 };
    private readonly Button _deny = new() { Text = "Deny selected", Width = 125 };

    public ConfirmationsForm(SteamGuardAccount account, Action saveAccount)
    {
        _account = account;
        _saveAccount = saveAccount;
        Text = $"Confirmations — {account.AccountName}";
        Width = 760;
        Height = 470;
        StartPosition = FormStartPosition.CenterParent;

        _list.Columns.Add("Type", 120);
        _list.Columns.Add("Action", 215);
        _list.Columns.Add("Details", 370);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Controls.AddRange([_refresh, _approve, _deny]);
        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(buttons);

        _refresh.Click += async (_, _) => await RefreshAsync();
        _approve.Click += async (_, _) => await ApplyAsync(true);
        _deny.Click += async (_, _) => await ApplyAsync(false);
        Shown += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_account.IdentitySecret))
        {
            ShowError("This account has no identity secret, so confirmations cannot be signed.");
            return;
        }
        if (_account.Session is null || string.IsNullOrWhiteSpace(_account.Session.RefreshToken))
        {
            ShowError("This account has no saved Steam session. Close this window and use Sign in / refresh.");
            return;
        }

        SetBusy(true, "Loading pending Steam confirmations...");
        try
        {
            if (_account.Session.IsAccessTokenExpired())
            {
                await _account.Session.RefreshAccessToken(true);
                _saveAccount();
            }

            Confirmation[] confirmations = await _account.FetchConfirmationsAsync();
            _list.Items.Clear();
            foreach (Confirmation confirmation in confirmations)
            {
                string details = confirmation.Summary is { Count: > 0 }
                    ? string.Join(" • ", confirmation.Summary)
                    : string.Empty;
                var item = new ListViewItem(confirmation.ConfType.ToString()) { Tag = confirmation };
                item.SubItems.Add(confirmation.Headline ?? string.Empty);
                item.SubItems.Add(details);
                _list.Items.Add(item);
            }
            _status.Text = confirmations.Length == 0
                ? "No pending confirmations."
                : $"{confirmations.Length} pending confirmation{(confirmations.Length == 1 ? "" : "s")}. Select entries to approve or deny.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message.Contains("Needs Authentication", StringComparison.OrdinalIgnoreCase)
                ? "Steam rejected the saved session. Close this window and use Sign in / refresh."
                : "Could not load confirmations: " + ex.Message);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async Task ApplyAsync(bool approve)
    {
        Confirmation[] selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<Confirmation>()
            .ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Select at least one confirmation first.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string verb = approve ? "APPROVE" : "DENY";
        string summary = string.Join(Environment.NewLine, selected.Take(5).Select(x => $"• {x.ConfType}: {x.Headline}"));
        if (selected.Length > 5)
            summary += $"{Environment.NewLine}• ...and {selected.Length - 5} more";
        if (MessageBox.Show(this,
                $"{verb} these {selected.Length} Steam action{(selected.Length == 1 ? "" : "s")}?{Environment.NewLine}{Environment.NewLine}{summary}",
                $"Confirm {verb.ToLowerInvariant()}", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        SetBusy(true, approve ? "Approving selected confirmations..." : "Denying selected confirmations...");
        try
        {
            foreach (Confirmation confirmation in selected)
            {
                bool success = approve
                    ? await _account.AcceptConfirmation(confirmation)
                    : await _account.DenyConfirmation(confirmation);
                if (!success)
                    throw new InvalidOperationException($"Steam rejected the action for: {confirmation.Headline}");
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Steam could not {verb.ToLowerInvariant()} the selection: {ex.Message}");
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string text)
    {
        _refresh.Enabled = !busy;
        _approve.Enabled = !busy;
        _deny.Enabled = !busy;
        _status.Text = text;
        UseWaitCursor = busy;
    }

    private void ShowError(string message)
    {
        _status.Text = message;
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
