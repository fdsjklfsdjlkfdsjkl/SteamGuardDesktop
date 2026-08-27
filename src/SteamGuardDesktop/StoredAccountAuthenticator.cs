using SteamAuth;
using SteamKit2.Authentication;

namespace SteamGuardDesktop;

internal sealed class StoredAccountAuthenticator : IAuthenticator
{
    private readonly SteamGuardAccount _account;
    private readonly Control _owner;

    public StoredAccountAuthenticator(SteamGuardAccount account, Control owner)
    {
        _account = account;
        _owner = owner;
    }

    public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
    {
        if (previousCodeWasIncorrect)
        {
            int remaining = SteamGuardCodeGenerator.SecondsRemaining(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await Task.Delay(TimeSpan.FromSeconds(remaining + 1));
        }

        return await _account.GenerateSteamGuardCodeAsync();
    }

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
    {
        string? code = null;
        RunOnUi(() => code = PromptDialog.Show(_owner, "Steam email code",
            previousCodeWasIncorrect
                ? $"That code was rejected. Enter the newest code sent to {email}:"
                : $"Enter the code sent to {email}:"));
        return Task.FromResult(code ?? string.Empty);
    }

    // Returning false makes SteamKit use the generated device code instead of
    // waiting for approval from another authenticator device.
    public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);

    private void RunOnUi(Action action)
    {
        if (_owner.InvokeRequired)
            _owner.Invoke(action);
        else
            action();
    }
}
