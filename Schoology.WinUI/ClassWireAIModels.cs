namespace Schoology.WinUI;

internal enum ClassWireAIProvider { Gemini, OpenAI, Anthropic, Ollama }

internal static class ClassWireAIProviderDefaults
{
    internal static string Model(ClassWireAIProvider provider) => provider switch
    {
        ClassWireAIProvider.Gemini => "gemini-3.8-flash",
        ClassWireAIProvider.OpenAI => "gpt-4.1-mini",
        ClassWireAIProvider.Anthropic => "claude-sonnet-4-20250514",
        ClassWireAIProvider.Ollama => "qwen3:8b",
        _ => "gemini-3.8-flash"
    };
}

internal sealed class ClassWireAIMessage(string role, string content)
{
    public string Role { get; set; } = role;
    public string Content { get; set; } = content;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ClassWireAIConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New conversation";
    public List<ClassWireAIMessage> Messages { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    internal static ClassWireAIConversation Create() => new();
}

internal sealed class ClassWireAISettings
{
    public ClassWireAIProvider Provider { get; set; } = ClassWireAIProvider.Gemini;
    public string Model { get; set; } = ClassWireAIProviderDefaults.Model(ClassWireAIProvider.Gemini);
    public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
}
