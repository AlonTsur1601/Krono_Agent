using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed class AgentWorker : IDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> pending = new();
    public event Action<string, string, JsonNode?>? Event;
    private readonly SemaphoreSlim writer = new(1);
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("Create the worker on the UI thread.");
    private bool disposed;
    public AgentWorker()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Engine");
        process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(folder, "node.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder } };
        process.StartInfo.ArgumentList.Add(Path.Combine(folder, "engine.cjs"));
        process.Start();
        _ = Task.Run(async () => { try { while (await process.StandardOutput.ReadLineAsync() is { } line) { var message = JsonNode.Parse(line)!; var id = message["id"]!.ToString(); if (message["event"] is { } type) await Deliver(id, type.ToString(), message["data"]); else if (pending.TryRemove(id, out var waiter)) { if (message["error"] is { } error) waiter.TrySetException(new InvalidOperationException(error.ToString())); else waiter.TrySetResult(message["result"]); } } } catch (Exception error) { foreach (var waiter in pending.Values) waiter.TrySetException(error); } finally { foreach (var waiter in pending.Values) waiter.TrySetException(new IOException("Agent worker stopped.")); } });
        _ = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() is not null) { /* SDK diagnostics may contain request data: never forward to UI logs. */ } });
    }
    // Drain each event on the UI thread before accepting the following completion.
    // Otherwise a fast response can finish while its text is still queued, losing it.
    private Task Deliver(string id, string type, JsonNode? data)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() => { try { if (!disposed) Event?.Invoke(id, type, data); delivered.TrySetResult(); } catch (Exception error) { delivered.TrySetException(error); } })) delivered.TrySetException(new IOException("The UI dispatcher has stopped."));
        return delivered.Task;
    }
    public async Task Send(string id, string method, object data) { await writer.WaitAsync(); try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, data })); await process.StandardInput.FlushAsync(); } finally { writer.Release(); } }
    public async Task<JsonNode?> Request(string id, string method, object data) { var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = waiter; try { await Send(id, method, data); return await waiter.Task; } finally { pending.TryRemove(id, out _); } }
    public void Dispose() { if (disposed) return; disposed = true; Event = null; try { process.StandardInput.Close(); if (!process.WaitForExit(1500)) process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); }
}
