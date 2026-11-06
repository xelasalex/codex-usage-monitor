using System.Runtime.InteropServices;
using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
namespace TaskbarWidget;
public sealed class TaskbarTracker {
  [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string? t);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L,T,R,B; }
  public event Action<Rect>? Moved;
  public void Poll() {
    var r = GetTrayRect();
    if (r is not null) Moved?.Invoke(r.Value);
  }
  public static Rect? GetTrayRect() {
    var h = FindWindow("Shell_TrayWnd", null); if (h == IntPtr.Zero) return null;
    if (!GetWindowRect(h, out var r)) return null;
    return new Rect(r.L, r.T, r.R - r.L, r.B - r.T);
  }
  public static Point Calc(Rect tray, Size win, int gap = 8) {
    bool horizontal = tray.Width >= tray.Height;
    if (tray.Top == 0 && horizontal) return new(tray.Right - win.Width - gap, tray.Bottom); // arriba
    if (horizontal) return new(tray.Right - win.Width - gap, tray.Top - win.Height);        // abajo
    if (tray.Left == 0) return new(tray.Right, tray.Bottom - win.Height - gap);              // izquierda
    return new(tray.Left - win.Width, tray.Bottom - win.Height - gap);                       // derecha
  }
  public static void SelfTest() {
    void C(bool c, string n){ if(!c) throw new Exception("tracker FAIL "+n); }
    var p = Calc(new Rect(0,1040,1920,40), new Size(200,40));
    C(p.X == 1920-200-8 && p.Y == 1000, "tray abajo");
    p = Calc(new Rect(0,0,1920,40), new Size(200,40));
    C(p.Y == 40, "tray arriba");
    p = Calc(new Rect(0,0,40,1080), new Size(200,40));
    C(p.X == 40, "tray izquierda");
    p = Calc(new Rect(1880,0,40,1080), new Size(200,40));
    C(p.X == 1880-200, "tray derecha");
    var d = ToDip(new Rect(0,2080,3840,80), 2.0, 2.0);
    C(d.X == 0 && d.Width == 1920 && d.Height == 40, "px a dip x2");
  }
  public static Rect ToDip(Rect px, double sx, double sy)
    => new(px.X / sx, px.Y / sy, px.Width / sx, px.Height / sy);
}
