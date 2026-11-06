using System.Text.Json;
namespace CodexCore;
public sealed record AuthState(bool SignedIn, string? PlanType, string? AuthMode);
public sealed class AuthService(ICodexRpc client) {
  public async Task<AuthState> CheckAsync(CancellationToken ct) {
    var result = await client.RequestAsync("account/read", new { }, ct);
    if (!result.TryGetProperty("account", out var a) || a.ValueKind != JsonValueKind.Object) return new(false, null, null);
    static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    return new(true, Text(a, "planType"), Text(a, "type"));
  }
  public async Task LogoutAsync(CancellationToken ct) {
    await client.RequestAsync("account/logout", new { }, ct);
    if ((await CheckAsync(ct)).SignedIn) throw new InvalidOperationException("Codex session is still active.");
  }
  public async Task SignInAsync(Action<Uri> openBrowser, CancellationToken ct) {
    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var completion = client.WaitNotifyAsync("account/login/completed", 180000, waitCts.Token);
    string? loginId = null; bool success = false;
    try {
      var login = await client.RequestAsync("account/login/start", new { type = "chatgpt" }, ct);
      loginId = login.GetProperty("loginId").GetString();
      openBrowser(SafeProcess.LoginUri(login.GetProperty("authUrl").GetString()));
      var done = await completion ?? throw new IOException("Login connection closed.");
      var result = done.GetProperty("params");
      if (result.GetProperty("loginId").GetString() != loginId || result.GetProperty("success").ValueKind != JsonValueKind.True
          || !(await CheckAsync(ct)).SignedIn) throw new InvalidOperationException("Login was not completed.");
      success = true;
    } finally {
      waitCts.Cancel(); try { await completion; } catch { }
      if (!success && loginId is not null) {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await client.RequestAsync("account/login/cancel", new { loginId }, cancel.Token, 2500); } catch { }
      }
    }
  }
}
