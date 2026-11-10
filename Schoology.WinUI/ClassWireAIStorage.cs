using System.Text.Json;
using Windows.Security.Credentials;

namespace Schoology.WinUI;

internal static class ClassWireAIStorage
{
    private const string VaultResource = "ClassWire.AI";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassWire", "AI");
    private static string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    private static string ConversationsPath => Path.Combine(DirectoryPath, "conversations.json");

    internal static ClassWireAISettings LoadSettings()
    {
        try { if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<ClassWireAISettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception ex) { App.WriteStartupLog("Could not load ClassWire AI settings.", ex); }
        return new();
    }

    internal static void SaveSettings(ClassWireAISettings settings)
    {
        try { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions)); }
        catch (Exception ex) { App.WriteStartupLog("Could not save ClassWire AI settings.", ex); }
    }

    internal static List<ClassWireAIConversation> LoadConversations()
    {
        try
        {
            if (File.Exists(ConversationsPath))
                return (JsonSerializer.Deserialize<List<ClassWireAIConversation>>(File.ReadAllText(ConversationsPath)) ?? [])
                    .OrderByDescending(item => item.UpdatedAt).Take(30).ToList();
        }
        catch (Exception ex) { App.WriteStartupLog("Could not load ClassWire AI conversations.", ex); }
        return [];
    }

    internal static void SaveConversations(IEnumerable<ClassWireAIConversation> conversations)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(ConversationsPath, JsonSerializer.Serialize(conversations.OrderByDescending(item => item.UpdatedAt).Take(30), JsonOptions));
        }
        catch (Exception ex) { App.WriteStartupLog("Could not save ClassWire AI conversations.", ex); }
    }

    internal static string? LoadKey(ClassWireAIProvider provider)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(VaultResource, provider.ToString());
            credential.RetrievePassword();
            return credential.Password;
        }
        catch { return null; }
    }

    internal static string? LoadGoogleRefreshToken()
    {
        try
        {
            var credential = new PasswordVault().Retrieve("ClassWire Google OAuth", "refresh-token");
            credential.RetrievePassword();
            return credential.Password;
        }
        catch { return null; }
    }

    internal static void SaveGoogleRefreshToken(string refreshToken)
    {
        try
        {
            var vault = new PasswordVault();
            try { vault.Remove(vault.Retrieve("ClassWire Google OAuth", "refresh-token")); } catch { }
            if (!string.IsNullOrWhiteSpace(refreshToken))
                vault.Add(new PasswordCredential("ClassWire Google OAuth", "refresh-token", refreshToken.Trim()));
        }
        catch (Exception ex) { throw new InvalidOperationException("Could not save the Google OAuth refresh token securely in Windows Credential Manager.", ex); }
    }

    internal static void SaveKey(ClassWireAIProvider provider, string key)
    {
        try
        {
            var vault = new PasswordVault();
            try { vault.Remove(vault.Retrieve(VaultResource, provider.ToString())); } catch { }
            if (!string.IsNullOrWhiteSpace(key))
                vault.Add(new PasswordCredential(VaultResource, provider.ToString(), key.Trim()));
        }
        catch (Exception ex) { App.WriteStartupLog("Could not save the ClassWire AI provider key in Windows Credential Manager.", ex); }
    }
}
