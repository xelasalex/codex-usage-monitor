using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace CodexCore;
public interface ICodexRpc {
  Task<JsonElement> RequestAsync(string method, object? pars, CancellationToken ct, int timeoutMs = 10000);
  Task<JsonElement?> WaitNotifyAsync(string method, int timeoutMs, CancellationToken ct);
}
public sealed class CodexRpcException : Exception {
  public int? Code { get; }
  public CodexRpcException(int? code) : base("Codex rejected the request.") { Code = code; }
}
public sealed class CodexClient : ICodexRpc, IDisposable {
  readonly Process _p;
  readonly StreamWriter _in;
  readonly SemaphoreSlim _write = new(1, 1);
  readonly CancellationTokenSource _lifetime = new();
  readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
  readonly object _waitersLock = new();
  readonly List<(string method, TaskCompletionSource<JsonElement?> tcs)> _waiters = new();
  int _id, _readerOn, _disposed;
  public CodexClient() : this(StartInfo("app-server")) { }
  // Injection permits offline tests without accessing an account.
  public CodexClient(ProcessStartInfo info) {
    _p = Process.Start(info) ?? throw new IOException("Codex could not start.");
    _in = _p.StandardInput;
    _ = DrainErrorsAsync();
  }
  public event Action<JsonElement>? Notification;
  public static ProcessStartInfo StartInfo(string command) => SafeProcess.Codex(command);
  async Task DrainErrorsAsync() {
    try { var buffer = new char[4096]; while (await _p.StandardError.ReadAsync(buffer.AsMemory(), _lifetime.Token) != 0) { } }
    catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
  }
  public Task StartReaderAsync(CancellationToken ct) {
    if (Interlocked.Exchange(ref _readerOn, 1) != 0) return Task.CompletedTask;
    return Task.Run(async () => {
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
      try {
        var buffer = new char[4096]; var line = new StringBuilder(); int read;
        while ((read = await _p.StandardOutput.ReadAsync(buffer.AsMemory(), linked.Token)) > 0) {
          for (int i = 0; i < read; i++) {
            if (buffer[i] == '\n') { Dispatch(line.ToString().TrimEnd('\r')); line.Clear(); }
            else { if (line.Length >= 1_048_576) throw new IOException("Codex response too large."); line.Append(buffer[i]); }
          }
        }
      } catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
      finally { FailPending(); }
    });
  }
  void Dispatch(string line) {
    try {
      using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object) return;
      if (root.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var id)) {
        if (!_pending.TryRemove(id, out var reply)) return;
        if (root.TryGetProperty("error", out var error)) {
          int? code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c)
            && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : null;
          reply.TrySetException(new CodexRpcException(code));
        } else if (root.TryGetProperty("result", out var result)) reply.TrySetResult(result.Clone());
        else reply.TrySetException(new IOException("Invalid Codex response."));
      } else if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String) {
        var clone = root.Clone();
        lock (_waitersLock) foreach (var w in _waiters.Where(w => w.method == m.GetString())) w.tcs.TrySetResult(clone);
        try { Notification?.Invoke(clone); } catch { }
      }
    } catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { }
  }
  void FailPending() {
    foreach (var p in _pending) if (_pending.TryRemove(p.Key, out var tcs)) tcs.TrySetException(new IOException("Codex connection closed."));
    lock (_waitersLock) foreach (var w in _waiters) w.tcs.TrySetResult(null);
  }
  async Task SendAsync(object message, CancellationToken ct) {
    await _write.WaitAsync(ct);
    try {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
      await _in.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct);
      await _in.FlushAsync(ct);
    } finally { _write.Release(); }
  }
  public async Task<JsonElement> RequestAsync(string method, object? pars, CancellationToken ct, int timeoutMs = 10000) {
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token); deadline.CancelAfter(timeoutMs);
    int id = Interlocked.Increment(ref _id);
    var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); _pending[id] = reply;
    try { await SendAsync(new { method, id, @params = pars ?? new { } }, deadline.Token); return await reply.Task.WaitAsync(deadline.Token); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_lifetime.IsCancellationRequested) { throw new TimeoutException("Codex response timed out."); }
    finally { _pending.TryRemove(id, out _); }
  }
  public Task NotifyAsync(string method, CancellationToken ct) => SendAsync(new { method, @params = new { } }, ct);
  public async Task<JsonElement?> WaitNotifyAsync(string method, int timeoutMs, CancellationToken ct) {
    var reply = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_waitersLock) _waiters.Add((method, reply));
    try { return await reply.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct); }
    finally { lock (_waitersLock) _waiters.RemoveAll(w => w.tcs == reply); }
  }
  public static bool MatchesId(string line, int id) => TryGetId(line, out var got) && got == id;
  public static bool TryGetId(string line, out int id) {
    id = 0;
    try { using var d = JsonDocument.Parse(line); return d.RootElement.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out id); }
    catch { return false; }
  }
  public static bool TryGetMethod(string line, out string? method) {
    method = null;
    try { using var d = JsonDocument.Parse(line); if (d.RootElement.TryGetProperty("id", out _)) return false;
      if (d.RootElement.TryGetProperty("method", out var v) && v.ValueKind == JsonValueKind.String) method = v.GetString(); return method != null; }
    catch { return false; }
  }
  public void Dispose() {
    if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    _lifetime.Cancel(); FailPending();
    try { if (!_p.HasExited) _p.Kill(entireProcessTree: true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    _p.Dispose();
  }
}
