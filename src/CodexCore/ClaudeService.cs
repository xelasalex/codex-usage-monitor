using System.Text.Json;
using System.Text.Json.Nodes;
namespace CodexCore;

// Consume only the documented status-line data. Never open Claude credentials or transcripts.
public sealed record ClaudeSnapshot(CodexRateLimits Limits, double? ContextUsed, DateTimeOffset At);
public sealed record ClaudeBridgeState(bool Enabled, ClaudeSnapshot? Snapshot);
public sealed class ClaudeService {
  readonly string _directory;
  public ClaudeService(string? directory = null) => _directory = directory ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageMonitor", "ClaudeBridge");
  public static double? Percent(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object
    && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)
    && double.IsFinite(n) && n is >= 0 and <= 100 ? n : null;
  public static ClaudeSnapshot Parse(JsonElement root, DateTimeOffset now) {
    var rates = root.TryGetProperty("rate_limits", out var r) && r.ValueKind == JsonValueKind.Object ? r : default;
    RateLimitWindow? Window(string key, int minutes) {
      if (rates.ValueKind != JsonValueKind.Object || !rates.TryGetProperty(key, out var w)) return null;
      var used = Percent(w, "used_percentage"); if (used is null) return null;
      DateTimeOffset? reset = null;
      if (w.TryGetProperty("resets_at", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out var secs)) {
        try { reset = DateTimeOffset.FromUnixTimeSeconds(secs); } catch (ArgumentOutOfRangeException) { return null; }
        if (reset <= now) return null;
      }
      return new(key, used.Value, 100 - used.Value, minutes, reset);
    }
    var ctx = root.TryGetProperty("context_window", out var c) ? Percent(c, "used_percentage") : null;
    return new(new(Window("five_hour", 300), Window("seven_day", 10080)), ctx, now);
  }
  public static async Task<ClaudeSnapshot> ReadInputAsync(TextReader reader, CancellationToken ct) {
    var buffer = new char[131073]; int total = 0, count;
    while ((count = await reader.ReadAsync(buffer.AsMemory(total), ct)) > 0) {
      total += count;
      if (total > 131072) throw new InvalidDataException("Status data too large.");
    }
    using var doc = JsonDocument.Parse(new string(buffer, 0, total), new JsonDocumentOptions { MaxDepth = 32 });
    return Parse(doc.RootElement, DateTimeOffset.UtcNow);
  }
  T Locked<T>(Func<ClaudeBridgeState, (ClaudeBridgeState? Save, T Result)> action) {
    Directory.CreateDirectory(_directory);
    using var gate = AcquireLock();
    var path = Path.Combine(_directory, "usage.json");
    ClaudeBridgeState state = new(false, null);
    if (File.Exists(path)) {
      if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid usage cache.");
      state = JsonSerializer.Deserialize<ClaudeBridgeState>(File.ReadAllText(path)) ?? state;
    }
    var (save, result) = action(state);
    if (save is not null) AtomicFile.Write(path, JsonSerializer.Serialize(save));
    return result;
  }
  FileStream AcquireLock() {
    var path = Path.Combine(_directory, "usage.lock");
    for (int i = 0; ; i++) {
      try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
      catch (IOException) when (i < 30) { Thread.Sleep(10); }
    }
  }
  public void SetEnabled(bool enabled) => Locked(s => (new ClaudeBridgeState(enabled, null), true));
  public bool Publish(ClaudeSnapshot snapshot) => Locked(s => s.Enabled
    ? (new ClaudeBridgeState(true, snapshot), true) : ((ClaudeBridgeState?)null, false));
  public ClaudeBridgeState Read() => Locked(s => {
    var snapshot = s.Enabled ? s.Snapshot : null;
    var now = DateTimeOffset.UtcNow;
    if (snapshot is not null) {
      RateLimitWindow? Valid(RateLimitWindow? w) => w is not null && double.IsFinite(w.UsedPercent)
        && w.UsedPercent is >= 0 and <= 100 && (w.ResetsAt is null || w.ResetsAt > now)
        ? w with { RemainingPercent = 100 - w.UsedPercent } : null;
      if (snapshot.At > now.AddMinutes(5)) snapshot = null;
      else snapshot = snapshot with { Limits = new(Valid(snapshot.Limits.FiveHour), Valid(snapshot.Limits.Weekly)),
        ContextUsed = snapshot.ContextUsed is double n && double.IsFinite(n) && n is >= 0 and <= 100 ? n : null };
    }
    return ((ClaudeBridgeState?)null, new ClaudeBridgeState(s.Enabled, snapshot));
  });
  public static bool InstallStatusLine(string executable, string? settingsPath = null) {
    if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)
        || executable.IndexOfAny(['"', '\'', '`', '$', '%', '!', '&', '|', '<', '>', '\r', '\n']) >= 0)
      throw new InvalidOperationException("Invalid bridge executable.");
    var path = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var gate = new FileStream(path + ".widget.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    if (File.Exists(path) && new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Settings too large.");
    var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
    if (root is null) throw new InvalidDataException("Invalid settings.");
    var command = "\"" + executable.Replace('\\', '/') + "\" --claude-statusline";
    if (root["statusLine"] is JsonObject existing && existing["command"]?.GetValue<string>() == command) return true;
    // Never replace an existing status line, including malformed/unknown configurations.
    if (root.ContainsKey("statusLine")) return false;
    root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command, ["refreshInterval"] = 30 };
    AtomicFile.Write(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return true;
  }
}
public static class AtomicFile {
  public static void Write(string path, string content) {
    var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
    Directory.CreateDirectory(directory);
    var temp = Path.Combine(directory, ".widget-" + Guid.NewGuid().ToString("N") + ".tmp");
    try {
      using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
        writer.Write(content); writer.Flush(); stream.Flush(true);
      }
      if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    } finally { if (File.Exists(temp)) File.Delete(temp); }
  }
}
