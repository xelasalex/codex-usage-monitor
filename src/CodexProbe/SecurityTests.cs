using System.Diagnostics;
using System.Text.Json;
using CodexCore;

static class SecurityTests {
  static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("ok: " + message); }
  static async Task Reject(Func<Task> action, string message) {
    try { await action(); } catch { Check(true, message); return; } throw new Exception("FAIL: " + message);
  }
  static JsonElement Json(string value) { using var d = JsonDocument.Parse(value); return d.RootElement.Clone(); }
  public static async Task RunAsync() {
    var rpc = new FakeRpc { RejectLogout = true };
    await Reject(() => new AuthService(rpc).LogoutAsync(default), "logout propagates rejection");
    rpc = new FakeRpc { RemainSignedIn = true };
    await Reject(() => new AuthService(rpc).LogoutAsync(default), "logout verifies account absent");
    rpc = new FakeRpc(); await new AuthService(rpc).LogoutAsync(default);
    Check(rpc.ReadCount == 1, "successful logout verified");
    foreach (var bad in new[] { "http://auth.openai.com/x", "file:///C:/x", "https://auth.openai.com.evil.test/x", "https://auth.openai.com@evil.test/x", "https://auth.openai.com:444/x", "cmd.exe", "https://auth.openai.com/#x" }) {
      await Reject(() => { SafeProcess.LoginUri(bad); return Task.CompletedTask; }, "reject unsafe login destination");
    }
    Check(SafeProcess.LoginUri("https://auth.openai.com/oauth/authorize?state=fixture").Scheme == "https", "official auth destination accepted");
    rpc = new FakeRpc { LoginSuccess = false };
    await Reject(() => new AuthService(rpc).SignInAsync(_ => { }, default), "failed login notification rejected");
    rpc = new FakeRpc { WrongLoginId = true };
    await Reject(() => new AuthService(rpc).SignInAsync(_ => { }, default), "foreign login notification rejected");
    rpc = new FakeRpc(); await new AuthService(rpc).SignInAsync(_ => { }, default);
    Check(rpc.WaitRegisteredBeforeLogin, "login waiter registered before request");

    var fixture = Path.Combine(Path.GetTempPath(), "widget-security-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixture);
    try {
      var first = Path.Combine(fixture, "first"); var second = Path.Combine(fixture, "second");
      Directory.CreateDirectory(first); Directory.CreateDirectory(second);
      File.WriteAllText(Path.Combine(first, "codex.exe"), "fixture"); File.WriteAllText(Path.Combine(second, "codex.cmd"), "fixture");
      var path = string.Join(Path.PathSeparator, new[] { "", ".", first, second });
      Check(SafeProcess.FindCli("codex", path) == Path.Combine(first, "codex.exe"), "PATH respects directory order and rejects relative entries");
      Check(SafeProcess.FindCli("codex", "." + Path.PathSeparator) is null, "no current directory fallback");
      var info = SafeProcess.Create(Path.Combine(second, "codex.cmd"), ["app-server"]);
      Check(Path.IsPathFullyQualified(info.FileName) && info.Arguments.StartsWith("/d /v:off /s /c"), "system shell with AutoRun disabled");
      await Reject(() => { SafeProcess.Create(Path.Combine(second, "bad%PATH%.cmd"), ["app-server"]); return Task.CompletedTask; }, "shell expansion rejected");
      var bridge = new ClaudeService(Path.Combine(fixture,"bridge"));
      var input = Json("""{"rate_limits":{"five_hour":{"used_percentage":0.5,"resets_at":4102444800},"seven_day":{"used_percentage":25}},"context_window":{"used_percentage":41},"email":"private@example.test","access_token":"FAKE_PRIVATE_MARKER","workspace":{"current_dir":"PRIVATE_PATH_MARKER"},"transcript_path":"PRIVATE_TRANSCRIPT_MARKER"}""");
      var snapshot = ClaudeService.Parse(input, DateTimeOffset.UtcNow);
      Check(snapshot.Limits.FiveHour?.RemainingPercent == 99.5, "official percentages are 0-100, including values below one");
      Check(!bridge.Publish(snapshot), "bridge disabled by default");
      bridge.SetEnabled(true); Check(bridge.Publish(snapshot), "connected bridge receives metrics");
      var cached = File.ReadAllText(Path.Combine(fixture,"bridge","usage.json"));
      Check(!cached.Contains("PRIVATE") && !cached.Contains("email") && !cached.Contains("workspace"), "cache persists allowlisted metrics only");
      bridge.SetEnabled(false);
      Check(!bridge.Publish(snapshot) && bridge.Read().Snapshot is null, "late callback cannot restore disconnected data");
      bridge.SetEnabled(true);
      await Task.WhenAll(Enumerable.Range(0,20).Select(_ => Task.Run(() => bridge.Publish(snapshot))));
      Check(bridge.Read().Snapshot?.Limits.Weekly?.RemainingPercent == 75, "concurrent cache writes remain valid");
      Check(ClaudeService.Parse(Json("""{"rate_limits":{"five_hour":{"used_percentage":101},"seven_day":{"used_percentage":20,"resets_at":1}}}"""),DateTimeOffset.UtcNow).Limits == new CodexRateLimits(null,null), "invalid and expired limits discarded");
      await Reject(async () => { await ClaudeService.ReadInputAsync(new StringReader(new string('x',131073)),default); }, "oversized status input rejected");
      var settings = Path.Combine(fixture,"settings.json"); var exe = Path.Combine(first,"codex.exe");
      File.WriteAllText(settings,"""{"otherSetting":true,"statusLine":{"type":"command","command":"existing"}}""");
      var before=File.ReadAllText(settings);
      Check(!ClaudeService.InstallStatusLine(exe,settings) && File.ReadAllText(settings)==before,"existing status line preserved");
      File.WriteAllText(settings,"""{"otherSetting":true}""");
      Check(ClaudeService.InstallStatusLine(exe,settings) && Json(File.ReadAllText(settings)).GetProperty("otherSetting").GetBoolean(),"setup preserves unrelated settings");
    } finally {
      var resolved = Path.GetFullPath(fixture);
      if (Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
          && Path.GetFileName(resolved).StartsWith("widget-security-")) Directory.Delete(resolved, true);
    }

    var process = new ProcessStartInfo(Environment.ProcessPath!) {
      RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
      UseShellExecute=false, CreateNoWindow=true
    };
    if (Path.GetFileNameWithoutExtension(process.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
      process.ArgumentList.Add(typeof(SecurityTests).Assembly.Location);
    process.ArgumentList.Add("--test-server");
    using var client = new CodexClient(process);
    using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    _ = client.StartReaderAsync(ct.Token);
    try { await client.RequestAsync("fail",new {},ct.Token); throw new Exception("Expected rejection."); }
    catch (CodexRpcException e) { Check(!e.ToString().Contains("PRIVATE") && e.Code == -1,"remote error payload never reaches exception"); }
    await Task.WhenAll(Enumerable.Range(0,100).Select(async n => {
      var reply=await client.RequestAsync("echo",new { n },ct.Token); if(reply.GetProperty("n").GetInt32()!=n)throw new Exception("Wrong reply.");
    }));
    Check(true,"100 concurrent requests serialized; stderr flood drained");
    using var cancel = new CancellationTokenSource(100);
    await Reject(() => client.RequestAsync("hang",new {},cancel.Token),"pending request cancellation");
  }
  public static async Task FakeServerAsync() {
    await Console.Error.WriteAsync(new string('x',131072));
    string? line;
    while((line=await Console.In.ReadLineAsync())!=null) {
      using var d=JsonDocument.Parse(line);var r=d.RootElement;
      if(!r.TryGetProperty("id",out var id))continue;
      var method=r.GetProperty("method").GetString();
      if(method=="hang")continue;
      object response = method=="fail" ? new { id=id.GetInt32(),error=new {code=-1,message="PRIVATE_TOKEN_MARKER",data="PRIVATE_URL_MARKER"}} : (object)new {id=id.GetInt32(),result=r.GetProperty("params").Clone()};
      await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
    }
  }
  sealed class FakeRpc : ICodexRpc {
    public bool RejectLogout,RemainSignedIn,WrongLoginId,WaitRegisteredBeforeLogin;
    public bool LoginSuccess=true;
    bool loggedOut; TaskCompletionSource<JsonElement?>? waiter;
    public int ReadCount;
    public Task<JsonElement> RequestAsync(string method,object? pars,CancellationToken ct,int timeoutMs=10000) {
      ct.ThrowIfCancellationRequested();
      if(method=="account/logout") { if(RejectLogout)throw new CodexRpcException(-1); loggedOut=true;return Task.FromResult(Json("{}")); }
      if(method=="account/read") {ReadCount++;return Task.FromResult(Json(loggedOut&&!RemainSignedIn?"{\"account\":null}":"{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\"}}"));}
      if(method=="account/login/start") {
        WaitRegisteredBeforeLogin=waiter!=null;
        waiter!.SetResult(Json(JsonSerializer.Serialize(new {method="account/login/completed",@params=new {loginId=WrongLoginId?"other":"fixture",success=LoginSuccess}})));
        return Task.FromResult(Json("{\"loginId\":\"fixture\",\"authUrl\":\"https://auth.openai.com/authorize\"}"));
      }
      return Task.FromResult(Json("{}"));
    }
    public Task<JsonElement?> WaitNotifyAsync(string method,int timeoutMs,CancellationToken ct) {
      waiter=new(TaskCreationOptions.RunContinuationsAsynchronously);return waiter.Task.WaitAsync(ct);
    }
  }
}
