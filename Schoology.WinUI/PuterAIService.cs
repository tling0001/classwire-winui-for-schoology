using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Schoology.WinUI;

internal sealed class PuterAIService : IDisposable
{
    private readonly WebView2 _webView;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;
    private TcpListener? _localBridgeListener;
    private readonly CancellationTokenSource _bridgeLifetime = new();
    private int _bridgePort;

    internal PuterAIService(WebView2 webView) => _webView = webView;

    internal async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(
            "const p=window.puter; if (!p.auth.isSignedIn()) { if (typeof p.ui?.authenticateWithPuter === 'function') await p.ui.authenticateWithPuter(); else await p.auth.signIn({request_auth:true}); } const user=await p.auth.getUser().catch(()=>null); if (!p.auth.isSignedIn()) throw new Error('Puter sign-in did not complete.'); return user?.username || user?.email || user?.name || 'Puter account';",
            TimeSpan.FromMinutes(5), cancellationToken);
        return result.ValueKind == JsonValueKind.String ? result.GetString() ?? "Puter account" : "Puter account";
    }

    internal async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var script = "const p=window.puter;if(!p.auth.isSignedIn())throw new Error('Connect your Puter account in AI settings first.');" +
            "const catalogue=await Promise.race([p.ai.listModels(),new Promise((_,reject)=>setTimeout(()=>reject(new Error('Puter model discovery timed out after 25 seconds. Check your connection and refresh the model list.')),25000))]);" +
            "return (Array.isArray(catalogue)?catalogue:[]).map(item=>typeof item==='string'?item:item?.id).filter(id=>typeof id==='string'&&id.length);";
        var result = await InvokeAsync(script, TimeSpan.FromSeconds(30), cancellationToken);
        if (result.ValueKind != JsonValueKind.Array) return [];
        return result.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value!.Length <= 160 &&
                value.All(ch => char.IsLetterOrDigit(ch) || "-_.:".Contains(ch)))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        var messagePayload = messages.Select(message => new
        {
            role = message.Role,
            content = message.Content,
            attachments = message.Attachments.Select(attachment => new
            {
                filename = attachment.FileName,
                mimeType = attachment.MimeType,
                dataBase64 = attachment.DataBase64,
                textContent = attachment.TextContent
            }).ToArray()
        }).ToArray();
        var payloadLiteral = JsonSerializer.Serialize(JsonSerializer.Serialize(new
        {
            model, messages = messagePayload, tools = toolPayload
        }));
        var script = "const args=JSON.parse(" + payloadLiteral + ");" +
            "const p=window.puter;if(!p.auth.isSignedIn())throw new Error('Connect your Puter account in AI settings first.');" +
            "const uploaded=[];" +
            "try{" +
            "const messages=[{role:'system',content:" + JsonSerializer.Serialize(ClassWireAIProviderClient.SystemPrompt) + "}];" +
            "for(const item of args.messages){" +
            "const parts=[];const extracted=(item.attachments||[]).map(a=>a.textContent?'[Contents of '+a.filename+']\\n'+a.textContent:'').filter(Boolean).join('\\n\\n');" +
            "const text=[item.content,extracted].filter(Boolean).join('\\n\\n');if(text)parts.push({type:'text',text});" +
            "for(const a of (item.attachments||[])){" +
            "if(!a.dataBase64)continue;" +
            "if(!(a.mimeType==='application/pdf'||String(a.mimeType||'').startsWith('image/'))){if(a.textContent)continue;throw new Error('Puter file input supports images, PDFs, and locally extracted text files here; choose Gemini or Anthropic for this file type.');}" +
            "const binary=atob(a.dataBase64);const bytes=new Uint8Array(binary.length);for(let i=0;i<binary.length;i++)bytes[i]=binary.charCodeAt(i);" +
            "const safe=(a.filename||'attachment').replace(/[^A-Za-z0-9._-]/g,'_');" +
            "const path='classwire-ai-temp/'+Date.now()+'-'+Math.random().toString(36).slice(2)+'-'+safe;" +
            "const stored=await p.fs.write(path,new Blob([bytes],{type:a.mimeType}),{createMissingParents:true,dedupeName:true});" +
            "const puterPath=stored?.path||path;uploaded.push(puterPath);parts.push({type:'file',puter_path:puterPath});" +
            "}" +
            "if(parts.length===0)throw new Error('This Puter message has no supported text or file content.');" +
            "messages.push({role:item.role,content:parts.length===1&&parts[0].type==='text'?text:parts});" +
            "}" +
            "const response=await p.ai.chat(messages,{model:args.model,tools:args.tools,toolChoice:'auto',normalize:true});" +
            "const message=response?.message||response||{};let content=message.content??response?.content??'';" +
            "if(Array.isArray(content))content=content.map(part=>part?.text||'').filter(Boolean).join('\\n');" +
            "const raw=message.tool_calls||message.toolCalls||response?.tool_calls||response?.toolCalls||[];" +
            "const toolCalls=raw.map(call=>{let value=call?.function?.arguments??call?.arguments??{};if(typeof value==='string'){try{value=JSON.parse(value)}catch{value={}}}return{name:call?.function?.name||call?.name,arguments:value}}).filter(call=>typeof call.name==='string');" +
            "return {text:typeof content==='string'?content:'',toolCalls};" +
            "}finally{await Promise.all(uploaded.map(path=>p.fs.delete(path).catch(()=>{})));}";
        var result = await InvokeAsync(script, TimeSpan.FromSeconds(180), cancellationToken);
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
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null, dataPath, new CoreWebView2EnvironmentOptions());
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source) ||
                        source.Scheme != Uri.UriSchemeHttp || !source.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        source.Port != _bridgePort) return;
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
            var bridgeUrl = await StartLocalBridgeAsync(cancellationToken);
            _webView.Source = new Uri(bridgeUrl);
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

    private async Task<string> StartLocalBridgeAsync(CancellationToken cancellationToken)
    {
        _localBridgeListener = new TcpListener(IPAddress.Loopback, 0);
        _localBridgeListener.Start();
        _bridgePort = ((IPEndPoint)_localBridgeListener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptBridgeConnectionsAsync(_localBridgeListener, _bridgeLifetime.Token));
        return "http://localhost:" + _bridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";
    }

    private static async Task AcceptBridgeConnectionsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => ServeBridgeRequestAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static async Task ServeBridgeRequestAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(requestLine)) return;
                string? line;
                var headerBytes = 0;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
                {
                    headerBytes += line.Length;
                    if (headerBytes > 16384) return;
                }
                const string html = "<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>ClassWire Puter</title></head><body><p role='status'>Loading Puter…</p><script src='https://js.puter.com/v2/'></script></body></html>";
                var isRoot = requestLine.StartsWith("GET / ", StringComparison.Ordinal) ||
                    requestLine.StartsWith("GET / HTTP/", StringComparison.Ordinal) ||
                    requestLine.StartsWith("GET /index.html ", StringComparison.Ordinal);
                var body = isRoot ? Encoding.UTF8.GetBytes(html) : Array.Empty<byte>();
                var status = isRoot ? "200 OK" : "404 Not Found";
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " +
                    body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken);
                if (body.Length > 0) await stream.WriteAsync(body, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        try { _bridgeLifetime.Cancel(); } catch { }
        try { _localBridgeListener?.Stop(); } catch { }
        try { _initializeGate.Dispose(); } catch { }
        try { _bridgeLifetime.Dispose(); } catch { }
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
