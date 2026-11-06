using System.Diagnostics;
namespace CodexCore;
public static class SafeProcess {
  public static string? FindCli(string name, string? searchPath = null) {
    if (name is not ("codex" or "claude")) throw new ArgumentException("Unsupported CLI.");
    foreach (var entry in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
      var dir = entry.Trim().Trim('"');
      if (!Path.IsPathFullyQualified(dir)) continue;
      foreach (var suffix in new[] { ".exe", ".cmd" }) {
        try { var full = Path.GetFullPath(Path.Combine(dir, name + suffix)); if (File.Exists(full)) return full; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { }
      }
    }
    return null;
  }
  public static ProcessStartInfo Codex(string command) {
    if (command is not ("app-server" or "--version")) throw new ArgumentException("Unsupported command.");
    var file = FindCli("codex") ?? throw new FileNotFoundException("Codex CLI unavailable.");
    var info = Create(file, [command]);
    info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
    info.StandardInputEncoding = info.StandardOutputEncoding = new System.Text.UTF8Encoding(false);
    return info;
  }
  public static ProcessStartInfo Create(string file, IReadOnlyList<string> args) {
    if (!Path.IsPathFullyQualified(file)) throw new ArgumentException("Absolute executable path required.");
    var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
    if (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) {
      // cmd expands percent variables inside quotes too. Reject shell syntax.
      static bool Safe(string s) => s.IndexOfAny(['"', '%', '!', '^', '&', '|', '<', '>', '\r', '\n']) < 0;
      if (!Safe(file) || args.Any(a => !Safe(a))) throw new ArgumentException("Unsupported command characters.");
      info.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
      info.Arguments = "/d /v:off /s /c \"\"" + file + "\" " + string.Join(" ", args.Select(a => "\"" + a + "\"")) + "\"";
    } else {
      if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Executable required.");
      info.FileName = file;
      foreach (var arg in args) info.ArgumentList.Add(arg);
    }
    return info;
  }
  public static Uri LoginUri(string? value) {
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
        || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
        || !(uri.IdnHost.Equals("auth.openai.com", StringComparison.OrdinalIgnoreCase)
             || uri.IdnHost.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)))
      throw new InvalidOperationException("Invalid authentication destination.");
    return uri;
  }
  public static void OpenLogin(Uri uri) {
    var validated = LoginUri(uri.AbsoluteUri);
    Process.Start(new ProcessStartInfo(validated.AbsoluteUri) { UseShellExecute = true });
  }
}
