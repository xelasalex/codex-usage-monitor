using System.Text.Json;
namespace CodexCore;
public sealed class RateLimitService(ICodexRpc c) {
  public event Action<CodexRateLimits>? Changed;
  public async Task RefreshAsync(CancellationToken ct) {
    var rl = await c.RequestAsync("account/rateLimits/read", null, ct);
    Changed?.Invoke(Parse(rl.TryGetProperty("rateLimits", out var s) ? s : rl));
  }
  public void OnNotification(JsonElement n) {
    // account/rateLimits/updated trae snapshot compatible con rateLimits
    try {
      var snap = n.TryGetProperty("params", out var p) ? p : n;
      var v = Parse(snap.TryGetProperty("rateLimits", out var s) ? s : snap);
      if (v.FiveHour is not null || v.Weekly is not null) Changed?.Invoke(v); // no pisar bueno con vacÃ­o
    } catch {}
  }
  static CodexRateLimits Parse(JsonElement snap) {
    RateLimitWindow? five = null, week = null;
    foreach (var (name, el) in Each(snap)) {
      var w = Mk(name, el);
      if (RateLimits.IsFiveHour(w.WindowDurationMinutes)) five = w;
      else if (RateLimits.IsWeekly(w.WindowDurationMinutes)) week = w;
    }
    return new(five, week);
  }
  static IEnumerable<(string, JsonElement)> Each(JsonElement s) {
    if (s.TryGetProperty("primary", out var p) && p.ValueKind != JsonValueKind.Null) yield return ("primary", p);
    if (s.TryGetProperty("secondary", out var q) && q.ValueKind != JsonValueKind.Null) yield return ("secondary", q);
  }
  static RateLimitWindow Mk(string t, JsonElement e) {
    double used = e.TryGetProperty("usedPercent", out var u) ? u.GetDouble() : 0;
    if (!double.IsFinite(used) || used < 0 || used > 100) throw new FormatException("Invalid usage percentage.");
    int? mins = e.TryGetProperty("windowDurationMins", out var m) && m.ValueKind != JsonValueKind.Null ? m.GetInt32() : null;
    long? rs = e.TryGetProperty("resetsAt", out var r) && r.ValueKind != JsonValueKind.Null ? r.GetInt64() : null;
    return new(t, used, RateLimits.Remaining(used), mins, RateLimits.ToTime(rs));
  }
}
