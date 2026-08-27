using SteamKit2.Authentication;

namespace SteamGuardDesktop;

internal sealed class DialogAuthenticator : IAuthenticator
{
    private readonly Control _owner;

    public DialogAuthenticator(Control owner) => _owner = owner;

    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
        Task.FromResult(Prompt("Steam Guard code", previousCodeWasIncorrect
            ? "That code was rejected. Enter the current code from the existing authenticator:"
            : "Enter the current code from the existing authenticator:") ?? string.Empty);

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
        Task.FromResult(Prompt("Email verification", previousCodeWasIncorrect
            ? $"That code was rejected. Enter the new code sent to {email}:"
            : $"Enter the code sent to {email}:") ?? string.Empty);

    public Task<bool> AcceptDeviceConfirmationAsync()
    {
        RunOnUi(() => MessageBox.Show(_owner,
            "Approve this sign-in in your existing Steam app, then click OK.",
            "Approve sign-in", MessageBoxButtons.OK, MessageBoxIcon.Information));
        return Task.FromResult(true);
    }

    private string? Prompt(string title, string message)
    {
        string? result = null;
        RunOnUi(() => result = PromptDialog.Show(_owner, title, message));
        return result;
    }

    private void RunOnUi(Action action)
    {
        if (_owner.InvokeRequired)
            _owner.Invoke(action);
        else
            action();
    }
}
