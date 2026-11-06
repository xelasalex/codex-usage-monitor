namespace CodexCore;
public sealed record RateLimitWindow(string Type, double UsedPercent, double RemainingPercent, int? WindowDurationMinutes, DateTimeOffset? ResetsAt);
public sealed record CodexAccount(string? Email, string? PlanType, string? AuthMode);
public sealed record CodexRateLimits(RateLimitWindow? FiveHour, RateLimitWindow? Weekly);
public static class RateLimits {
  public static double Remaining(double used) => 100.0 - used;
  public static bool IsReset(double? prevRem, double? curRem) => prevRem is not null && curRem is not null && curRem - prevRem > 15;
  public static bool IsFiveHour(int? mins) => mins is >=270 and <=330;
  public static bool IsWeekly(int? mins) => mins is >=9072 and <=11088;
  public static DateTimeOffset? ToTime(long? v) => v switch {
    null => null,
    > 1_000_000_000_000L => DateTimeOffset.FromUnixTimeMilliseconds((long)v),
    _ => DateTimeOffset.FromUnixTimeSeconds((long)v) };
}
public static class BarColors {
  // umbrales: >=50 verde, >=20 amarillo, >=5 naranja, <5 rojo
  public static string ColorFor(double remaining)
    => remaining >= 50 ? "#16C60D" : remaining >= 20 ? "#FFB900" : remaining >= 5 ? "#FF8C00" : "#E81123";
  public static bool CrossedBelow(double? prev, double cur, double line = 20)
    => prev is not null && prev >= line && cur < line;
}
// Chime de dos tonos sintetizado (880Hz 0.15s + 660Hz 0.25s, 22050Hz mono 16-bit): sin assets ni deps
public static class Chime {
  public static byte[] Wav() {
    var data = new List<byte>();
    void Tone(double freq, double secs) {
      int n = (int)(22050 * secs);
      for (int i = 0; i < n; i++) {
        double env = 1.0 - (double)i / n;
        short s = (short)(Math.Sin(2 * Math.PI * freq * i / 22050) * 12000 * env);
        data.Add((byte)(s & 0xFF)); data.Add((byte)((s >> 8) & 0xFF));
      }
    }
    Tone(880, 0.15); Tone(660, 0.25);
    using var ms = new MemoryStream();
    void Str(string v) { foreach (var c in v) ms.WriteByte((byte)c); }
    void U32(uint v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)((v >> 8) & 0xFF)); ms.WriteByte((byte)((v >> 16) & 0xFF)); ms.WriteByte((byte)((v >> 24) & 0xFF)); }
    void U16(ushort v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)((v >> 8) & 0xFF)); }
    Str("RIFF"); U32((uint)(36 + data.Count)); Str("WAVE"); Str("fmt "); U32(16); U16(1); U16(1); U32(22050); U32(22050 * 2); U16(2); U16(16);
    Str("data"); U32((uint)data.Count);
    ms.Write(data.ToArray(), 0, data.Count);
    return ms.ToArray();
  }
}
