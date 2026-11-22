using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed class AgentWorker : IDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> pending = new();
    public event Action<string, string, JsonNode?>? Event;
    internal Func<string, JsonNode, Task<object>>? BrowserRequest { get; set; }
    private readonly SemaphoreSlim writer = new(1);
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("Create the worker on the UI thread.");
    private readonly Task output;
    private readonly Task diagnostics;
    private volatile bool disposed;
    private volatile bool stopped;
    public AgentWorker()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Engine");
        process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(folder, "node.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = folder, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        } };
        process.StartInfo.ArgumentList.Add(Path.Combine(folder, "engine.cjs"));
        process.Start();
        output = Task.Run(ReadOutput);
        diagnostics = Task.Run(DrainDiagnostics);
    }

    async Task ReadOutput()
    {
        try {
            while (await process.StandardOutput.ReadLineAsync() is { } line) {
                var message = JsonNode.Parse(line);
                if (message?["id"] is not { } requestId) continue;
                var id = requestId.ToString();
                if (message["event"] is { } type) {
                    try { await Deliver(id, type.ToString(), message["data"]); }
                    catch (Exception error) {
                        App.RecordFault("Agent event", error);
                        if (pending.TryRemove(id, out var failed)) failed.TrySetException(new IOException("The app could not display an agent update."));
                    }
                } else if (pending.TryRemove(id, out var waiter)) {
                    if (message["error"] is { } error) waiter.TrySetException(new InvalidOperationException(error.ToString()));
                    else waiter.TrySetResult(message["result"]);
                }
            }
        } catch (Exception error) {
            if (!disposed) App.RecordFault("Agent transport", error);
        } finally {
            stopped = true;
            foreach (var (id, _) in pending) {
                if (pending.TryRemove(id, out var waiter)) waiter.TrySetException(new IOException("Agent worker stopped."));
            }
        }
    }

    async Task DrainDiagnostics()
    {
        try { await process.StandardError.BaseStream.CopyToAsync(Stream.Null); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    // Drain each event on the UI thread before accepting the following completion.
    // Otherwise a fast response can finish while its text is still queued, losing it.
    private Task Deliver(string id, string type, JsonNode? data)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => {
            try {
                if (!disposed) {
                    if (type == "browser" && data is not null) _ = AnswerBrowser(id, data);
                    else Event?.Invoke(id, type, data);
                }
                delivered.TrySetResult();
            } catch (Exception error) { delivered.TrySetException(error); }
        })) delivered.TrySetException(new IOException("The UI dispatcher has stopped."));
        return delivered.Task;
    }

    async Task AnswerBrowser(string run, JsonNode request)
    {
        object result;
        try {
            result = BrowserRequest is { } handler
                ? await handler(run, request)
                : new { success = false, error = "Open the browser workspace to use the agent browser." };
        } catch (Exception error) {
            App.RecordFault("Agent browser", error);
            result = new { success = false, error = "The browser could not complete that action." };
        }
        try { await Send(Guid.NewGuid().ToString(), "browser:result", new { run, id = request["id"]?.ToString(), result }); }
        catch (Exception error) when (error is IOException or InvalidOperationException) { }
    }
    public async Task Send(string id, string method, object data)
    {
        await writer.WaitAsync().ConfigureAwait(false);
        try {
            if (disposed || stopped) throw new IOException("Agent worker stopped.");
            var request = new { id, method, data };
            var line = method == "run"
                ? await Task.Run(() => JsonSerializer.Serialize(request)).ConfigureAwait(false)
                : JsonSerializer.Serialize(request);
            await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
        } finally { writer.Release(); }
    }

    public async Task<JsonNode?> Request(string id, string method, object data)
    {
        if (method == "run" && BrowserRequest is not null) {
            var request = data as JsonObject ?? JsonSerializer.SerializeToNode(data)!.AsObject();
            request["browserEnabled"] = true;
            data = request;
        }
        var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, waiter)) throw new InvalidOperationException("Request already pending.");
        try {
            await Send(id, method, data).ConfigureAwait(false);
            return await waiter.Task.ConfigureAwait(false);
        } finally { pending.TryRemove(id, out _); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Event = null;
        BrowserRequest = null;
        _ = Task.Run(Stop);
    }

    async Task Stop()
    {
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            await writer.WaitAsync(timeout.Token);
            try { process.StandardInput.Close(); }
            finally { writer.Release(); }
            await process.WaitForExitAsync(timeout.Token);
        } catch (OperationCanceledException) {
            try { process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception error) { App.RecordFault("Stopping agent worker", error); }
        } catch (Exception error) when (error is InvalidOperationException or IOException) {
            App.RecordFault("Stopping agent worker", error);
        } finally {
            try { await Task.WhenAll(output, diagnostics).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { }
            process.Dispose();
        }
    }
}
