using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Schoology.WinUI;

internal sealed record GoogleCloudProject(string ProjectId, string DisplayName, string ProjectNumber);
internal sealed record GoogleGeminiConnectResult(string Email, IReadOnlyList<GoogleCloudProject> Projects, string? ProjectListError);

internal static class GoogleGeminiSetupService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static string ClientId => GoogleOAuthConfiguration.ClientId;
    private static string? ClientSecret => string.IsNullOrWhiteSpace(GoogleOAuthConfiguration.ClientSecret) ? null : GoogleOAuthConfiguration.ClientSecret;

    internal static async Task<GoogleGeminiConnectResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("The Google OAuth client ID is not configured in this build.");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = "http://127.0.0.1:" + port + "/oauth2callback";
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
            var state = Guid.NewGuid().ToString("N");
            var query = new Dictionary<string, string>
            {
                ["client_id"] = ClientId, ["redirect_uri"] = redirectUri, ["response_type"] = "code",
                ["scope"] = "openid email https://www.googleapis.com/auth/cloud-platform",
                ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = state,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
            };
            var authorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&",
                query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var callbackClient = await listener.AcceptTcpClientAsync(timeout.Token);
            var callbackStream = callbackClient.GetStream();
            using var requestBytes = new MemoryStream();
            var buffer = new byte[4096];
            while (requestBytes.Length < 8192)
            {
                var read = await callbackStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                if (read <= 0) break;
                requestBytes.Write(buffer, 0, read);
                if (Encoding.UTF8.GetString(requestBytes.ToArray()).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var requestLine = Encoding.UTF8.GetString(requestBytes.ToArray()).Split("\r\n", StringSplitOptions.None).FirstOrDefault() ?? "";
            var callbackTarget = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "/";
            var callbackUri = new Uri("http://127.0.0.1:" + port + callbackTarget);
            var callbackQuery = ParseQuery(callbackUri.Query);
            var html = callbackQuery.ContainsKey("error")
                ? "<!doctype html><html><body><h2>Google sign-in was not completed</h2><p>You can close this tab and return to ClassWire.</p></body></html>"
                : "<!doctype html><html><body><h2>Google sign-in received</h2><p>Return to ClassWire. You can close this tab.</p></body></html>";
            var responseBytes = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\nContent-Length: " + responseBytes.Length + "\r\n\r\n");
            await callbackStream.WriteAsync(header, timeout.Token);
            await callbackStream.WriteAsync(responseBytes, timeout.Token);
            if (callbackQuery.TryGetValue("error", out var oauthError))
                throw new InvalidOperationException("Google sign-in was cancelled or rejected: " + oauthError);
            if (!callbackQuery.TryGetValue("state", out var callbackState) || callbackState != state)
                throw new InvalidOperationException("Google sign-in state validation failed. Please try again.");
            if (!callbackQuery.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Google did not return an authorization code.");

            var tokenParameters = new Dictionary<string, string>
            {
                ["client_id"] = ClientId, ["code"] = code, ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri
            };
            if (ClientSecret is not null) tokenParameters["client_secret"] = ClientSecret;
            using var tokenResponse = await Http.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(tokenParameters), cancellationToken);
            var tokenText = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("Google token exchange failed (HTTP " + (int)tokenResponse.StatusCode + "): " + Clip(tokenText, 900));
            using var tokenDocument = JsonDocument.Parse(tokenText);
            var token = tokenDocument.RootElement;
            var accessToken = ReadString(token, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("Google did not return an access token.");
            var refreshToken = ReadString(token, "refresh_token");
            if (!string.IsNullOrWhiteSpace(refreshToken))
                ClassWireAIStorage.SaveGoogleRefreshToken(refreshToken);
            else if (string.IsNullOrWhiteSpace(ClassWireAIStorage.LoadGoogleRefreshToken()))
                throw new InvalidOperationException("Google did not issue a refresh token. Revoke ClassWire in your Google Account security settings, then try connecting again.");

            var email = "Google account";
            try
            {
                var user = await GoogleApiAsync(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo", accessToken, null, cancellationToken);
                email = ReadString(user, "email") ?? email;
            }
            catch { }
            IReadOnlyList<GoogleCloudProject> projects = [];
            string? projectListError = null;
            try { projects = await ListProjectsWithTokenAsync(accessToken, cancellationToken); }
            catch (Exception ex) { projectListError = ex.Message; }
            return new GoogleGeminiConnectResult(email, projects, projectListError);
        }
        finally { listener.Stop(); }
    }

    internal static async Task<IReadOnlyList<GoogleCloudProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await RefreshAccessTokenAsync(cancellationToken);
        return await ListProjectsWithTokenAsync(accessToken, cancellationToken);
    }

    private static async Task<IReadOnlyList<GoogleCloudProject>> ListProjectsWithTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        var projects = new List<GoogleCloudProject>();
        string? pageToken = null;
        for (var pageNumber = 0; pageNumber < 20; pageNumber++)
        {
            var url = "https://cloudresourcemanager.googleapis.com/v3/projects?pageSize=100";
            if (!string.IsNullOrWhiteSpace(pageToken)) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var page = await GoogleApiAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
            if (page.TryGetProperty("projects", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (ReadString(item, "state") != "ACTIVE") continue;
                    var id = ReadString(item, "projectId");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    projects.Add(new GoogleCloudProject(id, ReadString(item, "displayName") ?? id, ReadString(item, "projectNumber") ?? ""));
                }
            }
            pageToken = ReadString(page, "nextPageToken");
            if (string.IsNullOrWhiteSpace(pageToken)) break;
        }
        return projects.OrderBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        var refreshToken = ClassWireAIStorage.LoadGoogleRefreshToken();
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Google is not connected yet. Choose Connect Google and authorize ClassWire first.");
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = ClientId, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token"
        };
        if (ClientSecret is not null) parameters["client_secret"] = ClientSecret;
        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(parameters), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Google authorization expired or was revoked (HTTP " + (int)response.StatusCode + "). Reconnect your Google account and try again.");
        using var document = JsonDocument.Parse(text);
        var accessToken = ReadString(document.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Google did not return an access token. Reconnect your account.");
        var rotated = ReadString(document.RootElement, "refresh_token");
        if (!string.IsNullOrWhiteSpace(rotated)) ClassWireAIStorage.SaveGoogleRefreshToken(rotated);
        return accessToken;
    }

    internal static async Task<string> CreateProjectAndGeminiKeyAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await RefreshAccessTokenAsync(cancellationToken);
        var projectId = "classwire-gemini-" + Guid.NewGuid().ToString("N")[..10];
        var operation = await GoogleApiAsync(HttpMethod.Post,
            "https://cloudresourcemanager.googleapis.com/v3/projects", accessToken,
            new { projectId, displayName = "ClassWire Gemini" }, cancellationToken);
        var operationName = ReadString(operation, "name");
        if (!string.IsNullOrWhiteSpace(operationName))
            _ = await WaitOperationAsync("https://cloudresourcemanager.googleapis.com/v3", operationName, accessToken, cancellationToken);

        JsonElement project = default;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                project = await GoogleApiAsync(HttpMethod.Get,
                    "https://cloudresourcemanager.googleapis.com/v3/projects/" + Uri.EscapeDataString(projectId),
                    accessToken, null, cancellationToken);
                if (ReadString(project, "state") == "ACTIVE") break;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("HTTP 404", StringComparison.Ordinal) && attempt < 29)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        if (ReadString(project, "state") != "ACTIVE")
            throw new InvalidOperationException("Google created the project but it is not active yet. Wait a moment, refresh projects, and try again.");

        await CreateGeminiKeyAsync(projectId, cancellationToken);
        return projectId;
    }

    internal static async Task CreateGeminiKeyAsync(string projectId, CancellationToken cancellationToken = default)
    {
        projectId = projectId.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(projectId, "^[a-z][a-z0-9-]{4,28}[a-z0-9]$"))
            throw new InvalidOperationException("Enter a valid Google Cloud project ID.");
        var accessToken = await RefreshAccessTokenAsync(cancellationToken);
        var project = await GoogleApiAsync(HttpMethod.Get,
            "https://cloudresourcemanager.googleapis.com/v3/projects/" + Uri.EscapeDataString(projectId),
            accessToken, null, cancellationToken);
        var projectNumber = ReadString(project, "projectNumber");
        if (string.IsNullOrWhiteSpace(projectNumber) ||
            !System.Text.RegularExpressions.Regex.IsMatch(projectNumber, "^[0-9]+$"))
            throw new InvalidOperationException("Google Cloud did not return a valid numeric project number for " + projectId + ".");

        foreach (var service in new[] { "iam.googleapis.com", "apikeys.googleapis.com", "generativelanguage.googleapis.com" })
            await EnableServiceAsync(projectId, service, accessToken, cancellationToken);

        var serviceAccountEmail = "classwire-gemini@" + projectId + ".iam.gserviceaccount.com";
        var serviceAccountUrl = "https://iam.googleapis.com/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/serviceAccounts/" + Uri.EscapeDataString(serviceAccountEmail);
        JsonElement serviceAccount;
        try { serviceAccount = await GoogleApiAsync(HttpMethod.Get, serviceAccountUrl, accessToken, null, cancellationToken); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("HTTP 404", StringComparison.Ordinal))
        {
            var createUrl = "https://iam.googleapis.com/v1/projects/" + Uri.EscapeDataString(projectId) + "/serviceAccounts?accountId=classwire-gemini";
            try
            {
                serviceAccount = await GoogleApiAsync(HttpMethod.Post, createUrl, accessToken, new
                {
                    serviceAccount = new
                    {
                        displayName = "ClassWire Gemini",
                        description = "Dedicated service-account identity for this user's ClassWire Gemini authorization key. No service-account private key is created."
                    }
                }, cancellationToken);
            }
            catch (InvalidOperationException createError) when (createError.Message.Contains("HTTP 409", StringComparison.Ordinal))
            {
                serviceAccount = await GoogleApiAsync(HttpMethod.Get, serviceAccountUrl, accessToken, null, cancellationToken);
            }
        }
        serviceAccountEmail = ReadString(serviceAccount, "email") ?? serviceAccountEmail;

        var accountReady = false;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                _ = await GoogleApiAsync(HttpMethod.Get, serviceAccountUrl, accessToken, null, cancellationToken);
                accountReady = true;
                break;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("HTTP 404", StringComparison.Ordinal) && attempt < 14)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        if (!accountReady)
            throw new InvalidOperationException("Google is still preparing the new service account. Wait a minute and try again.");

        string? existingKeyName = null;
        string? pageToken = null;
        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var listUrl = "https://apikeys.googleapis.com/v2/projects/" + Uri.EscapeDataString(projectNumber) + "/locations/global/keys?pageSize=100";
            if (!string.IsNullOrWhiteSpace(pageToken)) listUrl += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var keyList = await GoogleApiAsync(HttpMethod.Get, listUrl, accessToken, null, cancellationToken);
            if (keyList.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keys.EnumerateArray())
                {
                    var bound = ReadString(key, "serviceAccountEmail") == serviceAccountEmail;
                    var targets = key.TryGetProperty("restrictions", out var restrictions) &&
                        restrictions.TryGetProperty("apiTargets", out var apiTargets) && apiTargets.ValueKind == JsonValueKind.Array
                        ? apiTargets : default;
                    var geminiOnly = targets.ValueKind == JsonValueKind.Array && targets.GetArrayLength() == 1 &&
                        ReadString(targets[0], "service") == "generativelanguage.googleapis.com";
                    if (bound && geminiOnly) { existingKeyName = ReadString(key, "name"); break; }
                }
            }
            if (!string.IsNullOrWhiteSpace(existingKeyName)) break;
            pageToken = ReadString(keyList, "nextPageToken");
            if (string.IsNullOrWhiteSpace(pageToken)) break;
        }

        var keyName = existingKeyName;
        if (string.IsNullOrWhiteSpace(keyName))
        {
            var createUrl = "https://apikeys.googleapis.com/v2/projects/" + Uri.EscapeDataString(projectNumber) + "/locations/global/keys";
            JsonElement operation = default;
            string? lastBindingError = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5 * Math.Pow(2, attempt - 1)), cancellationToken);
                    _ = await GoogleApiAsync(HttpMethod.Get, serviceAccountUrl, accessToken, null, cancellationToken);
                }
                try
                {
                    operation = await GoogleApiAsync(HttpMethod.Post, createUrl, accessToken, new
                    {
                        displayName = "ClassWire Gemini authorization key",
                        serviceAccountEmail,
                        restrictions = new { apiTargets = new[] { new { service = "generativelanguage.googleapis.com" } } }
                    }, cancellationToken);
                    break;
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("service account", StringComparison.OrdinalIgnoreCase) &&
                    ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    lastBindingError = ex.Message;
                    if (attempt == 2) throw;
                }
            }
            var operationName = ReadString(operation, "name");
            if (string.IsNullOrWhiteSpace(operationName))
                throw new InvalidOperationException(lastBindingError ?? "Google did not return the Gemini key-creation operation.");
            var created = await WaitOperationAsync("https://apikeys.googleapis.com/v2", operationName, accessToken, cancellationToken);
            keyName = ReadString(created, "name");
            if (string.IsNullOrWhiteSpace(keyName))
                throw new InvalidOperationException("Google created the key but did not return its resource name.");
        }

        var keyValue = await GoogleApiAsync(HttpMethod.Get, "https://apikeys.googleapis.com/v2/" + keyName + "/keyString",
            accessToken, null, cancellationToken);
        var geminiKey = ReadString(keyValue, "keyString");
        if (string.IsNullOrWhiteSpace(geminiKey) || geminiKey.Length < 8)
            throw new InvalidOperationException("Google did not return the Gemini authorization key string.");
        ClassWireAIStorage.SaveKey(ClassWireAIProvider.Gemini, geminiKey);
    }

    private static async Task EnableServiceAsync(string projectId, string service, string accessToken, CancellationToken cancellationToken)
    {
        var resource = "https://serviceusage.googleapis.com/v1/projects/" + Uri.EscapeDataString(projectId) + "/services/" + Uri.EscapeDataString(service);
        var current = await GoogleApiAsync(HttpMethod.Get, resource, accessToken, null, cancellationToken);
        if (ReadString(current, "state") == "ENABLED") return;
        var operation = await GoogleApiAsync(HttpMethod.Post, resource + ":enable", accessToken, null, cancellationToken);
        var name = ReadString(operation, "name");
        if (!string.IsNullOrWhiteSpace(name))
            _ = await WaitOperationAsync("https://serviceusage.googleapis.com/v1", name, accessToken, cancellationToken);
    }

    private static async Task<JsonElement> WaitOperationAsync(string baseUrl, string operationName, string accessToken, CancellationToken cancellationToken)
    {
        var url = baseUrl.TrimEnd('/') + "/" + operationName.TrimStart('/');
        for (var attempt = 0; attempt < 90; attempt++)
        {
            var result = await GoogleApiAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
            if (result.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
            {
                if (result.TryGetProperty("error", out var error))
                    throw new InvalidOperationException("Google Cloud operation failed: " + error.GetRawText());
                return result.TryGetProperty("response", out var response) ? response.Clone() : result;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new InvalidOperationException("Google Cloud is still processing the request. Wait a moment and try again.");
    }

    private static async Task<JsonElement> GoogleApiAsync(HttpMethod method, string url, string accessToken, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = text;
            try
            {
                using var errorDocument = JsonDocument.Parse(text);
                if (errorDocument.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    detail = message.GetString() ?? text;
            }
            catch { }
            throw new InvalidOperationException("Google Cloud request failed (HTTP " + (int)response.StatusCode + "): " + Clip(detail, 900));
        }
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { });
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : "";
            result[key] = value;
        }
        return result;
    }

    private static string? ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() : null;

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Clip(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";
}
