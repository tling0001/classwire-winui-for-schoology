using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Schoology.WinUI;

internal sealed class PuterAIService
{
    private readonly WebView2 _webView;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    internal PuterAIService(WebView2 webView) => _webView = webView;

    internal async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(
            "const p=window.puter; if (!p.auth.isSignedIn()) { if (typeof p.ui?.authenticateWithPuter === 'function') await p.ui.authenticateWithPuter(); else await p.auth.signIn({request_auth:true}); } const user=await p.auth.getUser().catch(()=>null); if (!p.auth.isSignedIn()) throw new Error('Puter sign-in did not complete.'); return user?.username || user?.email || user?.name || 'Puter account';",
            TimeSpan.FromMinutes(5), cancellationToken);
        return result.ValueKind == JsonValueKind.String ? result.GetString() ?? "Puter account" : "Puter account";
    }

    internal async Task<ClassWireAICompletion> CompleteWithToolsAsync(
        string model, IReadOnlyList<ClassWireAIMessage> messages,
        IReadOnlyList<ClassWireAIToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        var toolPayload = tools.Select(tool => new
        {
            type = "function",
            function = new { name = tool.Name, description = tool.Description, parameters = tool.Parameters }
        }).ToArray();
        var messagePayload = new[] { new { role = "system", content = ClassWireAIProviderClient.SystemPrompt } }
            .Concat(messages.Select(message => new { role = message.Role, content = message.Content })).ToArray();
        var payloadLiteral = JsonSerializer.Serialize(JsonSerializer.Serialize(new { model, messages = messagePayload, tools = toolPayload }));
        var script = "const args=JSON.parse(" + payloadLiteral + ");" +
            "const p=window.puter;if(!p.auth.isSignedIn())throw new Error('Connect your Puter account in AI settings first.');" +
            "const response=await p.ai.chat(args.messages,{model:args.model,tools:args.tools,toolChoice:'auto',normalize:true});" +
            "const message=response?.message||response||{};let content=message.content??response?.content??'';" +
            "if(Array.isArray(content))content=content.map(part=>part?.text||'').filter(Boolean).join('\\n');" +
            "const raw=message.tool_calls||message.toolCalls||response?.tool_calls||response?.toolCalls||[];" +
            "const toolCalls=raw.map(call=>{let value=call?.function?.arguments??call?.arguments??{};if(typeof value==='string'){try{value=JSON.parse(value)}catch{value={}}}return{name:call?.function?.name||call?.name,arguments:value}}).filter(call=>typeof call.name==='string');" +
            "return {text:typeof content==='string'?content:'',toolCalls};";
        var result = await InvokeAsync(script, TimeSpan.FromSeconds(120), cancellationToken);
        var text = result.TryGetProperty("text", out var textValue) && textValue.ValueKind == JsonValueKind.String ? textValue.GetString() ?? "" : "";
        var calls = new List<ClassWireAIToolCall>();
        if (result.TryGetProperty("toolCalls", out var rawCalls) && rawCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in rawCalls.EnumerateArray())
            {
                var name = call.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var arguments = call.TryGetProperty("arguments", out var args) ? ReadArguments(args) : JsonSerializer.SerializeToElement(new { });
                calls.Add(new ClassWireAIToolCall("", name, arguments));
            }
        }
        return new ClassWireAICompletion(text, calls);
    }

    private async Task<JsonElement> InvokeAsync(string script, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Could not create the Puter request.");
        var idLiteral = JsonSerializer.Serialize(id);
        var wrapped = "(async()=>{try{const value=await (async()=>{" + script + "})();window.chrome.webview.postMessage({id:" + idLiteral + ",ok:true,value});}" +
            "catch(error){window.chrome.webview.postMessage({id:" + idLiteral + ",ok:false,error:String(error?.msg||error?.message||error)});}})();";
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(wrapped);
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Puter did not respond in time. Check your connection and try again.");
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            var dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassWire", "PuterWebView2");
            Directory.CreateDirectory(dataPath);
            _webView.CreationProperties = new() { UserDataFolder = dataPath };
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var document = JsonDocument.Parse(args.WebMessageAsJson);
                    var root = document.RootElement;
                    var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id) || !_pending.TryGetValue(id, out var completion)) return;
                    if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                    {
                        var value = root.TryGetProperty("value", out var result) ? result.Clone() : JsonSerializer.SerializeToElement(new { });
                        completion.TrySetResult(value);
                    }
                    else
                    {
                        var error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
                        completion.TrySetException(new InvalidOperationException(error ?? "Puter returned an unknown error."));
                    }
                }
                catch { }
            };
            _webView.NavigateToString("<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'></head><body><script src='https://js.puter.com/v2/'></script></body></html>");
            var ready = false;
            for (var attempt = 0; attempt < 120; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = await _webView.CoreWebView2.ExecuteScriptAsync("Boolean(window.puter && window.puter.ai && window.puter.auth)");
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { ready = true; break; }
                await Task.Delay(250, cancellationToken);
            }
            if (!ready) throw new InvalidOperationException("Could not load Puter.js. Check your internet connection and content restrictions.");
            _initialized = true;
        }
        finally { _initializeGate.Release(); }
    }

    private static JsonElement ReadArguments(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            try { using var parsed = JsonDocument.Parse(value.GetString() ?? "{}"); return parsed.RootElement.Clone(); }
            catch (JsonException) { return JsonSerializer.SerializeToElement(new { }); }
        }
        return value.ValueKind == JsonValueKind.Object ? value.Clone() : JsonSerializer.SerializeToElement(new { });
    }
}
