using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schoology.WinUI;

internal enum ClassWireAIProvider { Gemini, OpenAI, Anthropic, Ollama, Puter }

internal static class ClassWireAIProviderDefaults
{
    internal static string Model(ClassWireAIProvider provider) => provider switch
    {
        ClassWireAIProvider.Gemini => "gemini-3.8-flash",
        ClassWireAIProvider.OpenAI => "gpt-4.1-mini",
        ClassWireAIProvider.Anthropic => "claude-sonnet-4-20250514",
        ClassWireAIProvider.Ollama => "qwen3:8b",
        ClassWireAIProvider.Puter => "gpt-4.1-mini",
        _ => "gemini-3.8-flash"
    };
}

internal sealed class ClassWireAIAttachment
{
    public string FileName { get; set; } = "attachment";
    public string MimeType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    [JsonIgnore] public string? DataBase64 { get; set; }
    [JsonIgnore] public string? TextContent { get; set; }
}

internal sealed class ClassWireAIMessage(string role, string content)
{
    public string Role { get; set; } = role;
    public string Content { get; set; } = content;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ClassWireAIAttachment> Attachments { get; set; } = [];
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


internal sealed record ClassWireAIToolDefinition(string Name, string Description, JsonElement Parameters);
internal sealed record ClassWireAIToolCall(string Id, string Name, JsonElement Arguments);
internal sealed record ClassWireAICompletion(string Text, IReadOnlyList<ClassWireAIToolCall> ToolCalls);
internal sealed record ClassWireAIPageContext(string Page, string SelectedEntityKind, long SelectedEntityId,
    string SelectedEntityName, long AssignmentSectionId, long AssignmentId, long GradeItemId, bool AllowsSubmission);
