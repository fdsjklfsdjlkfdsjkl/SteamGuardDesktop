using SteamAuth;

namespace SteamGuardDesktop;

internal sealed class LoginApprovalsForm : Form
{
    private readonly LoginApprovalService _service;
    private readonly ListView _requests = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };
    private readonly Label _status = new() { AutoSize = true, Text = "Loading pending sign-in requests..." };
    private readonly Button _refresh = new() { Text = "Refresh", Width = 100 };
    private readonly Button _approve = new() { Text = "Approve", Width = 100, Enabled = false };
    private readonly Button _deny = new() { Text = "Deny", Width = 100, Enabled = false };

    public LoginApprovalsForm(SteamGuardAccount account, Action saveAccount)
    {
        _service = new LoginApprovalService(account, saveAccount);
        Text = $"Login approvals — {account.AccountName}";
        Width = 920;
        Height = 450;
        MinimumSize = new Size(720, 360);

        _requests.Columns.Add("Platform", 110);
        _requests.Columns.Add("Device", 180);
        _requests.Columns.Add("IP address", 135);
        _requests.Columns.Add("Location", 250);
        _requests.Columns.Add("Warnings", 170);

        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(255, 244, 204),
            Text = "Only approve a sign-in you just started. Check the device, IP address, and location first. Deny anything unexpected."
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(10),
            FlowDirection = FlowDirection.LeftToRight
        };
        footer.Controls.AddRange([_refresh, _approve, _deny, _status]);
        Controls.Add(_requests);
        Controls.Add(footer);
        Controls.Add(notice);

        Shown += async (_, _) => await RefreshRequestsAsync();
        _refresh.Click += async (_, _) => await RefreshRequestsAsync();
        _approve.Click += async (_, _) => await DecideAsync(true);
        _deny.Click += async (_, _) => await DecideAsync(false);
        _requests.SelectedIndexChanged += (_, _) => UpdateButtons();
    }

    private LoginApprovalRequest? Selected => _requests.SelectedItems.Count == 1
        ? _requests.SelectedItems[0].Tag as LoginApprovalRequest
        : null;

    private async Task RefreshRequestsAsync()
    {
        SetBusy(true, "Loading pending sign-in requests...");
        try
        {
            IReadOnlyList<LoginApprovalRequest> requests = await _service.GetPendingAsync();
            _requests.Items.Clear();
            foreach (LoginApprovalRequest request in requests)
            {
                var item = new ListViewItem(request.PlatformName) { Tag = request };
                item.SubItems.Add(string.IsNullOrWhiteSpace(request.DeviceName) ? "Unknown" : request.DeviceName);
                item.SubItems.Add(string.IsNullOrWhiteSpace(request.Ip) ? "Unknown" : request.Ip);
                item.SubItems.Add(request.Location);
                item.SubItems.Add(request.Warnings);
                _requests.Items.Add(item);
            }

            _status.Text = requests.Count == 0
                ? "No pending sign-in requests. Start the browser login, then click Refresh."
                : $"{requests.Count} pending request{(requests.Count == 1 ? "" : "s")}. Select one to review.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not load login approvals.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async Task DecideAsync(bool approve)
    {
        LoginApprovalRequest? request = Selected;
        if (request is null)
            return;

        string action = approve ? "APPROVE" : "DENY";
        string warning = request.Warnings == "None" ? string.Empty : $"\nSteam warnings: {request.Warnings}";
        string prompt = $"{action} this Steam sign-in?\n\n" +
                        $"Platform: {request.PlatformName}\n" +
                        $"Device: {(string.IsNullOrWhiteSpace(request.DeviceName) ? "Unknown" : request.DeviceName)}\n" +
                        $"IP address: {(string.IsNullOrWhiteSpace(request.Ip) ? "Unknown" : request.Ip)}\n" +
                        $"Location: {request.Location}{warning}\n\n" +
                        (approve ? "Only continue if you personally started this sign-in." : "The pending sign-in will be rejected.");
        if (MessageBox.Show(this, prompt, $"Confirm {action.ToLowerInvariant()}", MessageBoxButtons.YesNo,
                approve ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusy(true, $"Sending {action.ToLowerInvariant()} decision...");
        try
        {
            await _service.DecideAsync(request, approve);
            MessageBox.Show(this, approve ? "Steam sign-in approved." : "Steam sign-in denied.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshRequestsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetBusy(false, "The decision was not sent.");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _refresh.Enabled = !busy;
        _requests.Enabled = !busy;
        _status.Text = status;
        UseWaitCursor = busy;
        if (busy)
        {
            _approve.Enabled = false;
            _deny.Enabled = false;
        }
        else
        {
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        bool selected = Selected is not null && _requests.Enabled;
        _approve.Enabled = selected;
        _deny.Enabled = selected;
    }
}
