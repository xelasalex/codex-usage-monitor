using System.IO;
namespace TaskbarWidget;
public static class Autostart {
  static string Link => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Codex Usage Monitor.lnk");
  public static void Enable() {
    dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
    var ln = sh.CreateShortcut(Link);
    ln.TargetPath = Environment.ProcessPath; ln.WorkingDirectory = AppContext.BaseDirectory;
    ln.Description = "Codex Usage Monitor"; ln.Save();
  }
  public static void Disable() { try { File.Delete(Link); } catch {} }
  public static bool IsEnabled => File.Exists(Link);
  public static void SelfTest() {
    if (!Link.EndsWith("Codex Usage Monitor.lnk")) throw new Exception("autostart FAIL link");
  }
}
