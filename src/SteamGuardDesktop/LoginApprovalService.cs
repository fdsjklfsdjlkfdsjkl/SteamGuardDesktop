using System.Buffers.Binary;
using System.Security.Cryptography;
using ProtoBuf;
using SteamAuth;
using SteamKit2;
using SteamKit2.Internal;

namespace SteamGuardDesktop;

public static class LoginApprovalSignature
{
    public static byte[] Build(string sharedSecret, ulong steamId, ushort version, ulong clientId)
    {
        byte[] key = Convert.FromBase64String(sharedSecret);
        Span<byte> challenge = stackalloc byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(challenge, version);
        BinaryPrimitives.WriteUInt64LittleEndian(challenge[2..], clientId);
        BinaryPrimitives.WriteUInt64LittleEndian(challenge[10..], steamId);
        return HMACSHA256.HashData(key, challenge);
    }
}

internal sealed record LoginApprovalRequest(
    ulong ClientId,
    string Ip,
    string Geolocation,
    string City,
    string State,
    string Country,
    EAuthTokenPlatformType Platform,
    string DeviceName,
    bool LocationMismatch,
    bool HighUsageLogin,
    ESessionPersistence RequestedPersistence)
{
    public string PlatformName => Platform switch
    {
        EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient => "Steam Client",
        EAuthTokenPlatformType.k_EAuthTokenPlatformType_WebBrowser => "Web Browser",
        EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp => "Mobile App",
        _ => "Unknown"
    };

    public string Location
    {
        get
        {
            string[] parts = [City, State, Country];
            string location = string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(location) ? (string.IsNullOrWhiteSpace(Geolocation) ? "Unknown" : Geolocation) : location;
        }
    }

    public string Warnings
    {
        get
        {
            List<string> warnings = [];
            if (LocationMismatch) warnings.Add("location mismatch");
            if (HighUsageLogin) warnings.Add("high usage");
            return warnings.Count == 0 ? "None" : string.Join(", ", warnings);
        }
    }
}

internal sealed class LoginApprovalService
{
    private const string ServiceUrl = "https://api.steampowered.com/IAuthenticationService";
    private const ushort ChallengeVersion = 1;
    private static readonly HttpClient Client = CreateClient();
    private readonly SteamGuardAccount _account;
    private readonly Action _saveAccount;

    public LoginApprovalService(SteamGuardAccount account, Action saveAccount)
    {
        _account = account;
        _saveAccount = saveAccount;
    }

    public async Task<IReadOnlyList<LoginApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync();
        var sessions = await GetAsync<CAuthentication_GetAuthSessionsForAccount_Request,
            CAuthentication_GetAuthSessionsForAccount_Response>(
            "GetAuthSessionsForAccount", new CAuthentication_GetAuthSessionsForAccount_Request(), cancellationToken);

        var requests = new List<LoginApprovalRequest>();
        foreach (ulong clientId in sessions.client_ids)
        {
            CAuthentication_GetAuthSessionInfo_Response info;
            try
            {
                info = await PostAsync<CAuthentication_GetAuthSessionInfo_Request,
                    CAuthentication_GetAuthSessionInfo_Response>(
                    "GetAuthSessionInfo", new CAuthentication_GetAuthSessionInfo_Request { client_id = clientId }, cancellationToken);
            }
            catch (SteamWebApiException ex) when (ex.EResult is 9 or 27)
            {
                // Steam can briefly leave a completed or expired ID in the account-wide list.
                continue;
            }

            requests.Add(new LoginApprovalRequest(clientId, info.ip, info.geoloc, info.city, info.state,
                info.country, info.platform_type, info.device_friendly_name,
                info.requestor_location_mismatch, info.high_usage_login, info.requested_persistence));
        }

        return requests;
    }

    public async Task DecideAsync(LoginApprovalRequest request, bool approve, CancellationToken cancellationToken = default)
    {
        await EnsureAccessTokenAsync();
        if (_account.Session is null || _account.Session.SteamID == 0)
            throw new InvalidOperationException("The saved Steam session does not contain a Steam ID. Sign in again.");

        var input = new CAuthentication_UpdateAuthSessionWithMobileConfirmation_Request
        {
            version = ChallengeVersion,
            client_id = request.ClientId,
            steamid = _account.Session.SteamID,
            signature = LoginApprovalSignature.Build(_account.SharedSecret, _account.Session.SteamID,
                ChallengeVersion, request.ClientId),
            confirm = approve,
            persistence = approve && request.RequestedPersistence == ESessionPersistence.k_ESessionPersistence_Ephemeral
                ? ESessionPersistence.k_ESessionPersistence_Ephemeral
                : ESessionPersistence.k_ESessionPersistence_Persistent
        };

        _ = await PostAsync<CAuthentication_UpdateAuthSessionWithMobileConfirmation_Request,
            CAuthentication_UpdateAuthSessionWithMobileConfirmation_Response>(
            "UpdateAuthSessionWithMobileConfirmation", input, cancellationToken);
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (_account.Session is null || string.IsNullOrWhiteSpace(_account.Session.RefreshToken))
            throw new InvalidOperationException("No saved Steam session is available. Use Sign in / refresh first.");

        if (_account.Session.IsAccessTokenExpired())
        {
            await _account.Session.RefreshAccessToken(true);
            _saveAccount();
        }
    }

    private async Task<TResponse> GetAsync<TRequest, TResponse>(string method, TRequest input,
        CancellationToken cancellationToken)
    {
        string encoded = Convert.ToBase64String(Serialize(input)).Replace('+', '-').Replace('/', '_');
        string url = $"{ServiceUrl}/{method}/v1/?access_token={Uri.EscapeDataString(_account.Session!.AccessToken)}" +
                     $"&input_protobuf_encoded={Uri.EscapeDataString(encoded)}";
        using HttpResponseMessage response = await Client.GetAsync(url, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string method, TRequest input,
        CancellationToken cancellationToken)
    {
        string url = $"{ServiceUrl}/{method}/v1/?access_token={Uri.EscapeDataString(_account.Session!.AccessToken)}";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["input_protobuf_encoded"] = Convert.ToBase64String(Serialize(input))
        });
        using HttpResponseMessage response = await Client.PostAsync(url, form, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private static byte[] Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, value);
        return stream.ToArray();
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        string result = response.Headers.TryGetValues("x-eresult", out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? "unknown"
            : "unknown";
        string detail = response.Headers.TryGetValues("x-error_message", out IEnumerable<string>? messages)
            ? messages.FirstOrDefault() ?? string.Empty
            : string.Empty;

        if (!response.IsSuccessStatusCode || result != "1")
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Steam rejected the saved session. Use Sign in / refresh and try again.");

            string bodyDetail = string.Empty;
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
                string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                bodyDetail = System.Text.Encoding.UTF8.GetString(body);
                if (bodyDetail.Length > 240)
                    bodyDetail = bodyDetail[..240] + "...";
            }

            string status = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            int numericResult = int.TryParse(result, out int parsedResult) ? parsedResult : -1;
            throw new SteamWebApiException(numericResult,
                $"Steam rejected the request ({status}, EResult {result}). {detail} {bodyDetail}".Trim());
        }

        using var stream = new MemoryStream(body);
        return Serializer.Deserialize<T>(stream);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamGuardDesktop/1.2.1");
        return client;
    }
}

internal sealed class SteamWebApiException : InvalidOperationException
{
    public int EResult { get; }

    public SteamWebApiException(int eResult, string message) : base(message)
    {
        EResult = eResult;
    }
}
