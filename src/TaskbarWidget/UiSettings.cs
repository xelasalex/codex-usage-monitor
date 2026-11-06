using System.IO;
using System.Text.Json;
namespace TaskbarWidget;
// Solo prefs UI (posición + checks). Cero secretos: la sesión la posee Codex.
public sealed record UiSettings(double Left, double Top, bool Locked, bool Overlay, int? StartupDelaySecs, string WidgetSize, int? PollSecs, string Lang, double? RemFive, double? RemWeek, double? RemC5, double? RemCW, int? SoundVol, bool ShowClaudeContext = false) {
  static string Path => System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexUsageMonitor", "settings.json");
  public static UiSettings Load() {
    try {
      var j = File.ReadAllText(Path);
      var s = JsonSerializer.Deserialize<UiSettings>(j);
      if (s is null) return new UiSettings(double.NaN, double.NaN, true, false, 10, "M", 30, "es", null, null, null, null, 80);
      if (string.IsNullOrEmpty(s.WidgetSize)) s = s with { WidgetSize = "M" };
      if (string.IsNullOrEmpty(s.Lang)) s = s with { Lang = "es" };
      if (s.SoundVol is null) s = s with { SoundVol = 80 };
      if (!double.IsFinite(s.Left) || !double.IsFinite(s.Top)) s = s with { Left = double.NaN, Top = double.NaN };
      return s;
    } catch {}
    return new UiSettings(double.NaN, double.NaN, true, false, 10, "M", 30, "es", null, null, null, null, 80);
  }
  public void Save() {
    try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); CodexCore.AtomicFile.Write(Path, JsonSerializer.Serialize(this)); }
    catch {}
  }
  public static void SelfTest() {
    void C(bool c, string n) { if (!c) throw new Exception("settings FAIL " + n); }
    var s = new UiSettings(100.5, 200.5, false, true, 10, "M", 30, "es", null, null, null, null, 80);
    var back = JsonSerializer.Deserialize<UiSettings>(JsonSerializer.Serialize(s))!;
    C(back == s, "roundtrip");
    C(!double.IsFinite(double.NaN), "sin-posicion");
  }
}
