// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using CodexCore;

if (args.Contains("--test-server")) { await SecurityTests.FakeServerAsync(); return 0; }
if (args.Contains("--claude-statusline")) {
  try {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var snapshot = await ClaudeService.ReadInputAsync(Console.In, timeout.Token);
    if (new ClaudeService().Publish(snapshot))
      Console.WriteLine($"Claude 5H {snapshot.Limits.FiveHour?.RemainingPercent.ToString("0") ?? "—"}% · W {snapshot.Limits.Weekly?.RemainingPercent.ToString("0") ?? "—"}% restante");
    else Console.WriteLine("Claude");
    return 0;
  } catch { Console.WriteLine("Claude: sin datos"); return 0; }
}

if (args.Contains("--selftest"))
{
    void Check(bool c, string n) { if (!c) throw new Exception($"selftest FAIL: {n}"); Console.WriteLine($"ok: {n}"); }
    Check(RateLimits.Remaining(48) == 52, "remaining 48->52");
    Check(RateLimits.IsFiveHour(300) && !RateLimits.IsFiveHour(10080), "fivehour detect");
    Check(RateLimits.IsWeekly(10080) && !RateLimits.IsWeekly(300), "weekly detect");
    Check(!RateLimits.IsFiveHour(null) && !RateLimits.IsWeekly(null), "null mins unknown");
    Check(RateLimits.ToTime(null) is null, "null resetsAt");
    Check(RateLimits.ToTime(1_700_000_000)?.Year == 2023, "sec epoch");
    Check(RateLimits.ToTime(1_700_000_000_000L)?.Year == 2023, "ms epoch");
    await SecurityTests.RunAsync();
    Check(CodexClient.MatchesId("{\"id\":3,\"result\":{}}", 3) && !CodexClient.MatchesId("{\"method\":\"account/updated\",\"params\":{}}", 3), "match by id, skip notify");
    Check(BarColors.ColorFor(34) == "#FFB900", "amarillo 20-50");
    Check(BarColors.ColorFor(80) == "#16C60D" && BarColors.ColorFor(50) == "#16C60D", "verde 50+");
    Check(BarColors.ColorFor(19) == "#FF8C00" && BarColors.ColorFor(5) == "#FF8C00", "naranja 5-20");
    Check(BarColors.ColorFor(4) == "#E81123", "rojo bajo 5");
    Check(BarColors.CrossedBelow(21, 19) && !BarColors.CrossedBelow(19, 18) && !BarColors.CrossedBelow(null, 10), "cruce bajo 20");
    Check(CodexClient.TryGetId("{\"id\":7,\"result\":{}}", out var tid) && tid == 7, "route id");
    Check(!CodexClient.TryGetId("{\"method\":\"x\",\"params\":{}}", out _), "route no-id");
    Check(CodexClient.TryGetMethod("{\"method\":\"account/updated\",\"params\":{}}", out var tm) && tm == "account/updated", "route method");
    Console.WriteLine("selftest PASS");
    return 0;
}

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
try {
  using var client = new CodexClient();
  _ = client.StartReaderAsync(cts.Token);
  await client.RequestAsync("initialize", new { clientInfo = new { name = "codex_usage_monitor", title = "Codex Usage Monitor", version = "1.0.0" } }, cts.Token, 20000);
  await client.NotifyAsync("initialized", cts.Token);
  var auth = new AuthService(client);
  if (!(await auth.CheckAsync(cts.Token)).SignedIn) {
    Console.WriteLine("Completa el inicio de sesión en el navegador.");
    await auth.SignInAsync(SafeProcess.OpenLogin, cts.Token);
  }
  var limits = new RateLimitService(client);
  limits.Changed += value => { Print("5H", value.FiveHour); Print("W", value.Weekly); };
  await limits.RefreshAsync(cts.Token);
  return 0;
} catch (FileNotFoundException) { Console.WriteLine("Instala Codex CLI para conectar."); return 2; }
  catch (OperationCanceledException) { Console.WriteLine("Operación cancelada."); return 3; }
  catch { Console.WriteLine("No se pudo consultar Codex. Comprueba la sesión y reintenta."); return 4; }
static void Print(string name, RateLimitWindow? w) => Console.WriteLine(w is null ? $"{name}: sin datos"
  : $"{name}: {w.RemainingPercent:0}% restante · reinicio {w.ResetsAt?.LocalDateTime:g}");

