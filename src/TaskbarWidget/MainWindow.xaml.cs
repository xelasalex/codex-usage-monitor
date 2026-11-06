using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Threading;
using CodexCore;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
namespace TaskbarWidget;
public partial class MainWindow : Window {
  readonly TaskbarTracker _tracker = new();
  readonly PopupWindow _popup = new();
  WinForms.NotifyIcon? _tray;
  WinForms.ToolStripItem? _miOpen, _miRetry, _miExit;
  double? _seenFive, _seenWeek, _seenC5, _seenCW;
  string _size = "M"; double _barW = 110;
  int _pollSecs = 30;
  int _soundVol = 80;
  System.Windows.Media.MediaPlayer? _chime;
  int _delaySecs = 10;
  DispatcherTimer? _cdTimer;
  CodexClient? _client; AuthService? _auth; RateLimitService? _rl;
  readonly ClaudeService _claude = new();
  readonly SemaphoreSlim _codexGate = new(1, 1);
  bool _authBusy, _starting, _closing, _claudeEnabled, _showContext;
  readonly bool _testMode;
  int _epoch, _claudeEpoch;
  ClaudeSnapshot? _claudeSnapshot;
  CodexRateLimits? _lastClaude; bool _claudeOnline = true;
  
  
  readonly CancellationTokenSource _appCts = new();
  CancellationTokenSource? _liveCts; bool _live;
  DispatcherTimer? _pollTimer, _claudeTimer;
  DispatcherTimer? _trackTimer;
  CodexRateLimits? _last; bool _online = true; AuthState? _state; bool _noCodex;
  bool _locked = true, _placed, _overlay, _restoreOverlay, _dragging;
  const int WM_WINDOWPOSCHANGING = 0x0046;
  [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
  DispatcherTimer? _topTimer;
  static readonly IntPtr HWND_TOPMOST = new(-1);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  Point _downPt;
  public static bool WasClick(Point a, Point b) => (a - b).Length < 4;
  public static bool KeepTop(nint after) => (IntPtr)after != HWND_TOPMOST;
  public static void DragTest() {
    void C(bool c, string n) { if (!c) throw new Exception("drag FAIL " + n); }
    C(WasClick(new Point(10, 10), new Point(11, 11)), "quieto es clic");
    C(WasClick(new Point(10, 10), new Point(12, 12)), "3px es clic");
    C(!WasClick(new Point(10, 10), new Point(110, 10)), "100px es arrastre");
    C(KeepTop(0) && !KeepTop(-1), "top se fuerza, topmost se deja");
  }
  public MainWindow(bool testMode = false) {
    _testMode = testMode;
    InitializeComponent();
    var saved = testMode ? new UiSettings(100, 100, true, false, 0, "M", 30, "es", null, null, null, null, 80) : UiSettings.Load();
    _locked = saved.Locked;
    _seenFive = saved.RemFive; _seenWeek = saved.RemWeek;
    _seenC5 = saved.RemC5; _seenCW = saved.RemCW;
    _delaySecs = Math.Clamp(saved.StartupDelaySecs ?? 10, 0, 600);
    _showContext = saved.ShowClaudeContext;
    _popup.CkClaudeContext.IsChecked = _showContext;
    _popup.CkClaudeContext.Click += (_, _) => { _showContext = _popup.CkClaudeContext.IsChecked == true; SaveUi(); RenderClaude(); };
    L10n.Lang = string.IsNullOrEmpty(saved.Lang) ? "es" : saved.Lang;
    _popup.TbDelay.Text = _delaySecs.ToString();
    _popup.TbDelay.TextChanged += (_, _) => SaveUi();
    ApplyLang();
    _size = string.IsNullOrEmpty(saved.WidgetSize) ? "M" : saved.WidgetSize;
    ApplySize(_size); MarkSize();
    _pollSecs = NormPoll(saved.PollSecs ?? 30);
    MarkPoll();
    _soundVol = saved.SoundVol is >= 0 and <= 100 ? saved.SoundVol.Value : 80;
    _popup.SlVol.Value = _soundVol;
    _popup.SlVol.ValueChanged += (_, _) => { _soundVol = (int)_popup.SlVol.Value; SaveUi(); };
    _popup.BtnChimeTest.Click += (_, _) => PlayChime();
    _popup.BtnP15.Click += (_, _) => { ApplyPoll(15); };
    _popup.BtnP30.Click += (_, _) => { ApplyPoll(30); };
    _popup.BtnP60.Click += (_, _) => { ApplyPoll(60); };
    _popup.BtnP300.Click += (_, _) => { ApplyPoll(300); };
    _popup.BtnS.Click += (_, _) => { ApplySize("S"); SaveUi(); MarkSize(); };
    _popup.BtnM.Click += (_, _) => { ApplySize("M"); SaveUi(); MarkSize(); };
    _popup.BtnL.Click += (_, _) => { ApplySize("L"); SaveUi(); MarkSize(); };
    MarkLang();
    _popup.BtnEs.Click += (_, _) => { L10n.Lang = "es"; ApplyLang(); MarkLang(); SaveUi(); };
    _popup.BtnEn.Click += (_, _) => { L10n.Lang = "en"; ApplyLang(); MarkLang(); SaveUi(); };
    _popup.CkLock.IsChecked = _locked;
    Cursor = _locked ? Cursors.Arrow : Cursors.SizeAll;
    _restoreOverlay = saved.Overlay;
    if (double.IsFinite(saved.Left) && double.IsFinite(saved.Top) && OnScreen(saved.Left, saved.Top)) {
      Left = saved.Left; Top = saved.Top; _placed = true; // recordar posición
    }
    _tracker.Moved += trayPx => {
      if (_placed) return; // bloqueada: congelada donde esté (solo se coloca una vez al arrancar)
      var s = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
      var tray = TaskbarTracker.ToDip(trayPx, s.M11, s.M22);
      var p = TaskbarTracker.Calc(tray, new Size(Width, Height));
      Left = p.X; Top = p.Y;
      if (tray.Width >= tray.Height) Height = tray.Height; // mismo alto que la taskbar
      _placed = true;
      _trackTimer?.Stop(); _trackTimer = null; // colocado: no más polling de posición
    };
    if (!_placed) {
      var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
      t.Tick += (_, _) => _tracker.Poll();
      t.Start();
      _trackTimer = t;
      _tracker.Poll();
    }
    DpiChanged += (_, _) => _tracker.Poll();
    SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    MouseLeftButtonUp += (_, e) => {
      if (!_dragging && WasClick(_downPt, e.GetPosition(this))) TogglePopup();
      else if (_dragging) SaveUi(); // fin de arrastre: persistir posición
      _dragging = false;
    };
    LocationChanged += (_, _) => {
      if (!_popup.IsVisible) return;
      _popup.Left = Left + _popDx; _popup.Top = Top + _popDy; // anclado: conserva offset
    }; // popup anclado al widget
    PreviewMouseLeftButtonDown += (_, e) => { _downPt = e.GetPosition(this); _dragging = false; };
    _popup.MouseRightButtonUp += (_, _) => _popup.Hide(); // clic derecho en popup = cerrar popup
    PreviewMouseMove += (_, e) => {
      if (_locked || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
      if (!WasClick(_downPt, e.GetPosition(this))) { _dragging = true; try { DragMove(); } catch {} }
    };
    _popup.BtnRetry.Click += async (_, _) => { await SafeRefreshAsync(); await SafeRefreshClaudeAsync(); };
    _popup.BtnExit.Click += (_, _) => Close();
    _popup.BtnRestart.Click += (_, _) => App.RestartApp();
    _popup.BtnLogout.Click += async (_, _) => await DoLogoutAsync();
    _popup.BtnLogin.Click += async (_, _) => await DoLoginAsync();
    _popup.BtnClaudeLogin.Click += async (_, _) => await ConnectClaudeAsync();
    _popup.BtnClaudeLogout.Click += async (_, _) => await DisconnectClaudeAsync();
    _popup.CkAuto.IsChecked = Autostart.IsEnabled;
    _popup.CkAuto.Click += (_, _) => { if (_popup.CkAuto.IsChecked == true) Autostart.Enable(); else Autostart.Disable(); };
    _popup.CkLock.Click += (_, _) => {
      _locked = _popup.CkLock.IsChecked == true;
      Cursor = _locked ? Cursors.Arrow : Cursors.SizeAll;
      SaveUi();
    };
    _popup.CkOverlay.Click += (_, _) => {
      if (_popup.CkOverlay.IsChecked == true) EnableOverlay(); else DisableOverlay();
      SaveUi();
    };
    Loaded += async (_, _) => {
      if (_testMode) return;
      ApplyTheme(IsLightTheme());
      SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
      SetupTray();
      if (_restoreOverlay) { _popup.CkOverlay.IsChecked = true; EnableOverlay(); }
      if (_delaySecs > 0) {
        for (int i = _delaySecs; i > 0; i--) {
          _popup.LConn.Text = L10n.T("connecting", i);
          try { await Task.Delay(1000, _appCts.Token); } catch { return; }
        }
      }
      await BootAsync();
    };
    Closed += (_, _) => Cleanup();
  }
  void OnDisplaySettingsChanged(object? sender, EventArgs e) {
    if (!_closing) Dispatcher.BeginInvoke(() => { if (!_closing) _tracker.Poll(); });
  }
  void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
    if (!_closing) Dispatcher.BeginInvoke(() => { if (!_closing) ApplyTheme(IsLightTheme()); });
  }
  void Cleanup() {
    if (_closing) return;
    _closing = true; SaveUi(); StopLive(); _appCts.Cancel();
    _claudeEpoch++; _claudeTimer?.Stop(); _cdTimer?.Stop(); _trackTimer?.Stop(); _topTimer?.Stop();
    SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    _client?.Dispose(); _client = null; _tray?.Dispose(); _chime?.Close(); _popup.Close();
  }
  async Task BootAsync() {
    if (_starting || _closing) return;
    _starting = true;
    StartClaudeLoop();
    try {
      StopLive(); _client?.Dispose(); _auth = null; _noCodex = false;
      _client = new CodexClient();
      _ = _client.StartReaderAsync(_appCts.Token);
      await _client.RequestAsync("initialize", new { clientInfo = new { name = "codex_usage_monitor", title = "Codex Usage Monitor", version = "1.0.0" } }, _appCts.Token, 20000);
      await _client.NotifyAsync("initialized", _appCts.Token);
      _auth = new AuthService(_client);
      _state = await _auth.CheckAsync(_appCts.Token);
      if (_closing) return;
      if (!_state.SignedIn) ShowLogin(); else await StartLiveAsync();
    } catch (FileNotFoundException) { _noCodex = true; if (!_closing) ShowLogin(); }
      catch { if (!_closing) ShowLogin(); _client?.Dispose(); _client = null; _auth = null; }
    finally { _starting = false; }
  }
  async Task StartLiveAsync() {
    if (_live || _client is null || _closing) return;
    _live = true;
    var epoch = ++_epoch;
    _liveCts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
    _rl = new RateLimitService(_client);
    _rl.Changed += m => {
      if (_closing) return;
      Dispatcher.BeginInvoke(() => {
        if (!_closing && _live && epoch == _epoch) { _last = m; _online = true; RenderCodex(); }
      });
    };
    _client.Notification += OnNotification;
    await SafeRefreshAsync();
    if (!_live || epoch != _epoch || _closing) return;
    _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_pollSecs) };
    _pollTimer.Tick += async (_, _) => await SafeRefreshAsync();
    _pollTimer.Start();
  }
  void StopLive() {
    _live = false; _epoch++;
    _liveCts?.Cancel(); _liveCts?.Dispose(); _liveCts = null;
    _pollTimer?.Stop(); _pollTimer = null;
    if (_client is not null) _client.Notification -= OnNotification;
    _rl = null;
  }
  void OnNotification(System.Text.Json.JsonElement n) {
    if (_closing) return;
    var epoch = _epoch;
    Dispatcher.BeginInvoke(async () => {
      if (_closing || !_live || epoch != _epoch) return;
      try {
        if (!n.TryGetProperty("method", out var m)) return;
        if (m.GetString() == "account/rateLimits/updated") _rl?.OnNotification(n);
        else if (m.GetString() == "account/updated" && _auth is not null) {
          var state = await _auth.CheckAsync(_appCts.Token);
          if (epoch == _epoch && !_closing && !state.SignedIn) { StopLive(); ClearCodex(); ShowLogin(); }
        }
      } catch { }
    });
  }
  void ClearCodex() {
    _state = null; _last = null; _seenFive = _seenWeek = null; _online = false;
    RenderCodexBars(null, null); _popup.L5.Text = "5H: —"; _popup.LW.Text = "W: —";
    Txt5.ToolTip = TxtW.ToolTip = "—"; TickCountdown(); PaintTray(); SaveUi();
  }
  async Task SafeRefreshAsync() {
    if (!_live || _rl is null || _liveCts is null || _closing || !await _codexGate.WaitAsync(0)) return;
    var epoch = _epoch; var ct = _liveCts.Token; var service = _rl;
    try { await service.RefreshAsync(ct); }
    catch (OperationCanceledException) { }
    catch {
      if (epoch != _epoch || _closing) return;
      try {
        if (_auth is not null && !(await _auth.CheckAsync(ct)).SignedIn && epoch == _epoch) {
          StopLive(); ClearCodex(); ShowLogin(); return;
        }
      } catch { }
      if (epoch == _epoch && !_closing) { _online = false; RenderCodex(); }
    } finally { _codexGate.Release(); }
  }
  async Task DoLogoutAsync() {
    if (_authBusy || _auth is null || _closing) return;
    _authBusy = true; _popup.BtnLogout.IsEnabled = _popup.BtnLogin.IsEnabled = false;
    StopLive();
    try {
      await _codexGate.WaitAsync(_appCts.Token);
      try { await _auth.LogoutAsync(_appCts.Token); } finally { _codexGate.Release(); }
      if (!_closing) { ClearCodex(); ShowLogin(); }
    } catch {
      if (!_closing) {
        ClearCodex(); LoginPrompt.Text = L10n.T("logoutfailed"); LoginPrompt.Visibility = Visibility.Visible;
        _popup.LConn.Text = L10n.T("logoutfailed"); _popup.BtnLogin.Visibility = Visibility.Collapsed;
      }
    } finally { _authBusy = false; _popup.BtnLogout.IsEnabled = _popup.BtnLogin.IsEnabled = true; }
  }
  void StartClaudeLoop() {
    if (_claudeTimer is not null || _closing) return;
    _ = SafeRefreshClaudeAsync();
    _claudeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_pollSecs) };
    _claudeTimer.Tick += async (_, _) => await SafeRefreshClaudeAsync();
    _claudeTimer.Start();
  }
  bool _readingClaude;
  async Task SafeRefreshClaudeAsync() {
    if (_readingClaude || _closing) return;
    _readingClaude = true; var epoch = _claudeEpoch;
    try {
      var state = await Task.Run(() => _claude.Read());
      if (_closing || epoch != _claudeEpoch) return;
      _claudeEnabled = state.Enabled; _claudeSnapshot = state.Enabled ? state.Snapshot : null;
      _lastClaude = _claudeSnapshot?.Limits;
      _claudeOnline = _claudeSnapshot is not null && DateTimeOffset.UtcNow - _claudeSnapshot.At < TimeSpan.FromMinutes(2);
      RenderClaude();
    } catch { if (!_closing && epoch == _claudeEpoch) { _claudeOnline = false; RenderClaude(); } }
    finally { _readingClaude = false; }
  }
  async Task ConnectClaudeAsync() {
    _popup.BtnClaudeLogin.IsEnabled = false;
    try {
      var exe = Path.Combine(AppContext.BaseDirectory, "CodexProbe.exe");
      var installed = await Task.Run(() => ClaudeService.InstallStatusLine(exe));
      if (!installed && System.Windows.MessageBox.Show(L10n.T("claudeexisting"), "Codex Usage Monitor",
          MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
      await Task.Run(() => _claude.SetEnabled(true));
      _claudeEpoch++; _claudeEnabled = true; StartClaudeLoop(); await SafeRefreshClaudeAsync();
    } catch { _popup.LClConn.Text = L10n.T("claudesetuperror"); }
    finally { _popup.BtnClaudeLogin.IsEnabled = true; }
  }
  async Task DisconnectClaudeAsync() {
    _popup.BtnClaudeLogout.IsEnabled = false;
    try {
      _claudeEpoch++; // Reject reads started before disconnect.
      await Task.Run(() => _claude.SetEnabled(false)); // Serialized with bridge writers; no token operations.
      _claudeEpoch++; _claudeEnabled = false; _claudeSnapshot = null; _lastClaude = null; _seenC5 = _seenCW = null;
      SaveUi(); RenderClaude();
    } catch { _popup.LClConn.Text = L10n.T("disconnectfailed"); }
    finally { _popup.BtnClaudeLogout.IsEnabled = true; }
  }
  async Task DoLoginAsync() {
    if (_authBusy || _closing) return;
    if (_client is null || _auth is null) { await BootAsync(); if (_auth is null || _live) return; }
    _authBusy = true; _popup.BtnLogin.IsEnabled = _popup.BtnLogout.IsEnabled = false;
    StopLive();
    try {
      await _auth.SignInAsync(SafeProcess.OpenLogin, _appCts.Token);
      _state = await _auth.CheckAsync(_appCts.Token);
      await StartLiveAsync();
    } catch { if (!_closing) { ShowLogin(); _popup.LConn.Text = L10n.T("loginfailed"); } }
    finally { _authBusy = false; _popup.BtnLogin.IsEnabled = _popup.BtnLogout.IsEnabled = true; }
  }
  void ShowLogin() {
    LoginPrompt.Text = _noCodex ? L10n.T("nocodex") : L10n.T("loginprompt");
    LoginPrompt.Visibility = Visibility.Visible;
    _popup.BtnLogin.Visibility = Visibility.Visible;
    _popup.LConn.Text = _noCodex ? L10n.T("nocodex") : L10n.T("nosession");
  }
  void HideLogin() {
    LoginPrompt.Visibility = Visibility.Collapsed;
    _popup.BtnLogin.Visibility = Visibility.Collapsed;
  }
  void RenderLive() { RenderCodex(); RenderClaude(); }
  void RenderCodex() {
    if (!_live) return;
    HideLogin();
    var five = _last?.FiveHour; var week = _last?.Weekly;
    var fiveUnix = five?.ResetsAt?.ToUnixTimeSeconds(); var weekUnix = week?.ResetsAt?.ToUnixTimeSeconds();
    CheckReset(ref _seenFive, five?.RemainingPercent, "Codex 5H");
    CheckReset(ref _seenWeek, week?.RemainingPercent, "Codex W");
    RenderCodexBars(five?.RemainingPercent, week?.RemainingPercent);
    PaintBar(Bar5Wrap, five?.RemainingPercent ?? 100);
    PaintBar(BarWWrap, week?.RemainingPercent ?? 100);
    var gray = !_online;
    Txt5.Foreground = TxtW.Foreground = gray ? Brushes.Gray : Brushes.White;
    Txt5.ToolTip = Tip(five); TxtW.ToolTip = Tip(week);
    _popup.L5.Text = $"5H: {Line(five)}"; _popup.LW.Text = $"W: {Line(week)}";
    _popup.L5.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BarColors.ColorFor(five?.RemainingPercent ?? 100)));
    _popup.LW.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BarColors.ColorFor(week?.RemainingPercent ?? 100)));
    _popup.LConn.Text = _online ? L10n.T("connected") : L10n.T("offline");
    PaintTray();
  }
  void PaintTray() {
    var x = _last?.FiveHour?.RemainingPercent;
    var c = _lastClaude?.FiveHour?.RemainingPercent;
    var t = $"Codex {(x is null ? "–" : $"{x:0}%")} · Claude {(c is null ? "–" : $"{c:0}%")}";
    if (_tray is not null) _tray.Text = t.Length > 60 ? t[..60] : t;
  }
  void RenderClaude() {
    var c5 = _lastClaude?.FiveHour; var cw = _lastClaude?.Weekly;
    var c5Unix = c5?.ResetsAt?.ToUnixTimeSeconds(); var cwUnix = cw?.ResetsAt?.ToUnixTimeSeconds();
    CheckReset(ref _seenC5, c5?.RemainingPercent, "Claude 5H");
    CheckReset(ref _seenCW, cw?.RemainingPercent, "Claude W");
    RenderClaudeBars(c5?.RemainingPercent, cw?.RemainingPercent);
    PaintBar(BarC5Wrap, c5?.RemainingPercent ?? 100);
    PaintBar(BarCWWrap, cw?.RemainingPercent ?? 100);
    var cgray = !_claudeOnline;
    TxtC5.Foreground = TxtCW.Foreground = cgray ? Brushes.Gray : Brushes.White;
    TxtC5.ToolTip = Tip(c5); TxtCW.ToolTip = Tip(cw);
    _popup.LCl5.Text = $"5H: {Line(c5)}"; _popup.LClW.Text = $"W: {Line(cw)}";
    _popup.LCl5.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BarColors.ColorFor(c5?.RemainingPercent ?? 100)));
    _popup.LClW.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BarColors.ColorFor(cw?.RemainingPercent ?? 100)));
    _popup.LClConn.Text = !_claudeEnabled ? L10n.T("disconnected")
      : _claudeSnapshot is null ? L10n.T("claudewaiting") : _claudeOnline ? L10n.T("connected") : L10n.T("offline");
    _popup.LCtx.Visibility = _showContext ? Visibility.Visible : Visibility.Collapsed;
    _popup.LCtx.Text = _claudeSnapshot?.ContextUsed is double ctx ? $"CTX: {ctx:0}%" : "CTX: —";
    _popup.BtnClaudeLogin.Visibility = _claudeEnabled ? Visibility.Collapsed : Visibility.Visible;
    _popup.BtnClaudeLogout.Visibility = _claudeEnabled ? Visibility.Visible : Visibility.Collapsed;
    PaintTray();
  }
  static double WidthFor(string s) => s switch { "S" => 404, "L" => 584, _ => 484 };
  public static async Task SecuritySelfTestAsync() {
    var window = new MainWindow(testMode: true);
    try {
      void Check(bool value) { if (!value) throw new InvalidOperationException("UI security selftest failed."); }
      window._auth = new AuthService(new LogoutFixture(reject: true));
      window._live = true;
      window._state = new AuthState(true, "fixture", "chatgpt");
      await window.DoLogoutAsync();
      Check(!window._live && window._popup.LConn.Text == L10n.T("logoutfailed"));
      Check(window._popup.BtnLogin.Visibility == Visibility.Collapsed && window._popup.BtnLogout.IsEnabled);
      window._auth = new AuthService(new LogoutFixture(reject: false));
      await window.DoLogoutAsync();
      Check(window._state is null && window._last is null && window._popup.LConn.Text == L10n.T("nosession"));
      Check(window._popup.BtnLogin.Visibility == Visibility.Visible && !window._authBusy);
    } finally { window.Cleanup(); window.Close(); }
  }
  sealed class LogoutFixture(bool reject) : ICodexRpc {
    public Task<System.Text.Json.JsonElement> RequestAsync(string method, object? pars, CancellationToken ct, int timeoutMs = 10000) {
      if (reject) throw new CodexRpcException(-1);
      using var d = System.Text.Json.JsonDocument.Parse(method == "account/read" ? "{\"account\":null}" : "{}");
      return Task.FromResult(d.RootElement.Clone());
    }
    public Task<System.Text.Json.JsonElement?> WaitNotifyAsync(string method, int timeoutMs, CancellationToken ct)
      => Task.FromResult<System.Text.Json.JsonElement?>(null);
  }
  static double BarWidth(double total) => (total - 264) / 2;
  static int NormPoll(int v) => v is 15 or 30 or 60 or 300 ? v : 30;
  static string Countdown(RateLimitWindow? w) {
    if (w?.ResetsAt is null) return "—";
    var t = w.ResetsAt.Value - DateTimeOffset.Now;
    if (t <= TimeSpan.Zero) return L10n.T("restarting");
    return t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m"
      : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
      : $"{t.Minutes}m {t.Seconds}s";
  }
  static string Tip(RateLimitWindow? w) => w is null ? L10n.T("unknown")
    : $"{w.RemainingPercent:0}% {L10n.T("remaining")}, {L10n.T("resets")} {w.ResetsAt?.LocalDateTime.ToString() ?? L10n.T("unknown")}";
  static string Line(RateLimitWindow? w) => w is null ? "—"
    : $"{w.RemainingPercent:0}% {L10n.T("remaining")} — Restart: {(w.ResetsAt?.LocalDateTime.ToString("HH:mm dd/MM/yyyy") ?? L10n.T("unknown"))}";
  static Color Lighten(Color c, double f) => Color.FromRgb(
    (byte)Math.Min(255, c.R + (255 - c.R) * f),
    (byte)Math.Min(255, c.G + (255 - c.G) * f),
    (byte)Math.Min(255, c.B + (255 - c.B) * f));
  static LinearGradientBrush GlowBrush(double rem) {
    var base_ = (Color)ColorConverter.ConvertFromString(BarColors.ColorFor(rem));
    var g = new LinearGradientBrush(Lighten(base_, .35), base_, new Point(0, 0), new Point(1, 0));
    g.Freeze();
    return g;
  }
  public static void StyleTest() {
    void C(bool c, string n) { if (!c) throw new Exception("style FAIL " + n); }
    var l = Lighten(Color.FromRgb(0xE8, 0x11, 0x23), .35);
    C(l.R > 0xE8 && l.G > 0x11, "aclara rojo");
    C(GlowBrush(10) is LinearGradientBrush, "gradiente");
    C(Countdown(null) == "—", "cuenta nula");
    C(WidthFor("S") == 404 && WidthFor("M") == 484 && WidthFor("L") == 584 && WidthFor("?") == 484, "tallas");
    C(NormPoll(15) == 15 && NormPoll(60) == 60 && NormPoll(300) == 300 && NormPoll(99) == 30, "intervalos");
    C(Countdown(new RateLimitWindow("t", 0, 100, null, DateTimeOffset.Now.AddHours(-1))) == "reiniciando…", "cuenta pasada");
    L10n.Lang = "en";
    C(L10n.T("retry") == "Retry", "diccionario en");
    C(Countdown(new RateLimitWindow("t", 0, 100, null, DateTimeOffset.Now.AddHours(-1))) == "resetting…", "cuenta en ingles");
    L10n.Lang = "es";
  }
  static void PaintBar(Border bar, double rem) {
    bar.Background = GlowBrush(rem);
    var base_ = (Color)ColorConverter.ConvertFromString(BarColors.ColorFor(rem));
    bar.Effect = new DropShadowEffect { Color = base_, BlurRadius = 6, ShadowDepth = 0, Opacity = .55 };
  }
  public void RenderCodexBars(double? fiveRem, double? weekRem) {
    Bar5Wrap.Width = _barW * Math.Clamp(fiveRem ?? 0, 0, 100) / 100; Txt5.Text = fiveRem is null ? "–" : $"{fiveRem:0}%";
    BarWWrap.Width = _barW * Math.Clamp(weekRem ?? 0, 0, 100) / 100; TxtW.Text = weekRem is null ? "–" : $"{weekRem:0}%";
  }
  public void RenderClaudeBars(double? c5Rem, double? cwRem) {
    BarC5Wrap.Width = _barW * Math.Clamp(c5Rem ?? 0, 0, 100) / 100; TxtC5.Text = c5Rem is null ? "–" : $"{c5Rem:0}%";
    BarCWWrap.Width = _barW * Math.Clamp(cwRem ?? 0, 0, 100) / 100; TxtCW.Text = cwRem is null ? "–" : $"{cwRem:0}%";
  }
  void EnableOverlay() {
    _overlay = true;
    AssertTop();
    _topTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    _topTimer.Tick += (_, _) => AssertTop();
    _topTimer.Start();
  }
  void DisableOverlay() { _overlay = false; _topTimer?.Stop(); _topTimer = null; }
  protected override void OnSourceInitialized(EventArgs e) {
    base.OnSourceInitialized(e);
    ((HwndSource)PresentationSource.FromVisual(this)).AddHook(Hook);
  }
  IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) {
    if (_overlay && msg == WM_WINDOWPOSCHANGING) {
      var wp = Marshal.PtrToStructure<WINDOWPOS>(l);
      if (KeepTop((nint)wp.hwndInsertAfter)) {
        wp.hwndInsertAfter = HWND_TOPMOST;
        Marshal.StructureToPtr(wp, l, false);
      }
    }
    return IntPtr.Zero;
  }
  void AssertTop() {
    try {
      var h = new WindowInteropHelper(this).Handle;
      if (h != IntPtr.Zero) SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
    } catch {}
  }
  static bool OnScreen(double x, double y)
    => x >= SystemParameters.VirtualScreenLeft - 200 && x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
    && y >= SystemParameters.VirtualScreenTop - 100 && y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
  void SaveUi() {
    if (_testMode) return;
    if (int.TryParse(_popup.TbDelay.Text, out int v) && v >= 0 && v <= 600) _delaySecs = v;
    new UiSettings(Left, Top, _locked, _popup.CkOverlay.IsChecked == true, _delaySecs, _size, _pollSecs, L10n.Lang, _seenFive, _seenWeek, _seenC5, _seenCW, _soundVol, _showContext).Save();
  }
  void SetupTray() {
    _tray = new WinForms.NotifyIcon {
      Icon = System.Drawing.SystemIcons.Application, Text = "Codex Usage Monitor", Visible = true,
    };
    var menu = new WinForms.ContextMenuStrip();
    _miOpen = menu.Items.Add(L10n.T("open"), null, (_, _) => { TogglePopup(); });
    _miRetry = menu.Items.Add(L10n.T("retry"), null, async (_, _) => { await SafeRefreshAsync(); await SafeRefreshClaudeAsync(); });
    _miExit = menu.Items.Add(L10n.T("exit"), null, (_, _) => Close());
    _tray.ContextMenuStrip = menu;
  }
  void CheckReset(ref double? seenRem, double? curRem, string name) {
    if (curRem is null) return;
    if (seenRem is null) { seenRem = curRem; SaveUi(); return; } // siembra inicial sin avisar
    if (RateLimits.IsReset(seenRem, curRem)) {
      PlayChime();
      try { _tray?.ShowBalloonTip(4000, "Codex Usage Monitor", $"{name} {L10n.T("resetdone")}: {curRem:0}% {L10n.T("remaining")}", WinForms.ToolTipIcon.Info); }
      catch {}
      seenRem = curRem; SaveUi();
    } else seenRem = curRem;
  }
  void PlayChime() {
    try {
      var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexUsageMonitor", "chime.wav");
      if (!File.Exists(wav)) File.WriteAllBytes(wav, CodexCore.Chime.Wav());
      _chime ??= new System.Windows.Media.MediaPlayer();
      _chime.Volume = _soundVol / 100.0;
      _chime.Open(new Uri(wav));
      _chime.Play();
    } catch {}
  }
  static bool IsLightTheme()
    => (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) as int? ?? 1) != 0;
  void ApplyTheme(bool light) {
    // solo widget: el popup queda siempre oscuro con letras blancas
    var bg = light ? "#F3F3F3" : "#202020";
    var track = light ? "#D4D4D4" : "#3A3A3A";
    var text = light ? "#1B1B1B" : "#F3F3F3";
    var dim = light ? "#616161" : "#C7C7C7";
    Root.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
    Track5.Background = TrackW.Background = TrackC5.Background = TrackCW.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(track));
    LogoX.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#1B1B1B" : "#FFFFFF"));
    foreach (var tb in FindText(this)) {
      tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tb == Txt5 || tb == TxtW || tb == TxtC5 || tb == TxtCW ? text : dim));
    }
  }
  static IEnumerable<TextBlock> FindText(DependencyObject root) {
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
      var ch = VisualTreeHelper.GetChild(root, i);
      if (ch is TextBlock tb) yield return tb;
      foreach (var sub in FindText(ch)) yield return sub;
    }
  }
  double _popDx, _popDy;
  void ApplySize(string s) {
    _size = s;
    Width = WidthFor(s); _barW = BarWidth(Width);
    BarCol.Width = new GridLength(_barW);
    BarColC.Width = new GridLength(_barW);
    RenderCodexBars(_last?.FiveHour?.RemainingPercent, _last?.Weekly?.RemainingPercent);
    RenderClaudeBars(_lastClaude?.FiveHour?.RemainingPercent, _lastClaude?.Weekly?.RemainingPercent);
  }
  void ApplyPoll(int secs) {
    _pollSecs = NormPoll(secs);
    if (_pollTimer is not null) { _pollTimer.Interval = TimeSpan.FromSeconds(_pollSecs); }
    if (_claudeTimer is not null) { _claudeTimer.Interval = TimeSpan.FromSeconds(_pollSecs); }
    MarkPoll(); SaveUi();
  }
  void MarkPoll() {
    var on = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
    var off = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A3A"));
    _popup.BtnP15.Background = _pollSecs == 15 ? on : off;
    _popup.BtnP30.Background = _pollSecs == 30 ? on : off;
    _popup.BtnP60.Background = _pollSecs == 60 ? on : off;
    _popup.BtnP300.Background = _pollSecs == 300 ? on : off;
  }
  void MarkSize() {
    var on = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
    var off = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A3A"));
    _popup.BtnS.Background = _size == "S" ? on : off;
    _popup.BtnM.Background = _size == "M" ? on : off;
    _popup.BtnL.Background = _size == "L" ? on : off;
  }
  void MarkLang() {
    var on = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
    var off = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A3A"));
    _popup.BtnEs.Background = L10n.Lang == "es" ? on : off;
    _popup.BtnEn.Background = L10n.Lang == "en" ? on : off;
  }
  void ApplyLang() {
    _popup.CkAuto.Content = L10n.T("autostart");
    _popup.CkLock.Content = L10n.T("lock");
    _popup.CkOverlay.Content = L10n.T("overlay");
    _popup.LblDelay.Text = L10n.T("delay");
    _popup.LblSize.Text = L10n.T("size");
    _popup.LblPoll.Text = L10n.T("poll");
    _popup.LblSound.Text = L10n.T("sound");
    _popup.BtnChimeTest.Content = L10n.T("test");
    _popup.LblLang.Text = L10n.T("lang");
    _popup.BtnRetry.Content = L10n.T("retry");
    _popup.BtnRestart.Content = L10n.T("restart");
    _popup.BtnLogin.Content = L10n.T("login");
    _popup.BtnLogout.Content = L10n.T("logout");
    _popup.BtnExit.Content = L10n.T("exit");
    _popup.BtnClaudeLogin.Content = L10n.T("claudelogin");
    _popup.BtnClaudeLogout.Content = L10n.T("disconnect");
    _popup.CkClaudeContext.Content = L10n.T("showcontext");
    if (_miOpen is not null) _miOpen.Text = L10n.T("open");
    if (_miRetry is not null) _miRetry.Text = L10n.T("retry");
    if (_miExit is not null) _miExit.Text = L10n.T("exit");
    if (_last is not null) RenderLive();
    TickCountdown();
  }
  void TogglePopup() {
    if (_popup.IsVisible) { _popup.Hide(); _cdTimer?.Stop(); return; }
    try { _popup.Owner = this; } catch {} // el dueño debe estar visible; al cerrar el widget cae el popup
    var area = WorkAreaDip();
    var above = Top + Height / 2 > area.Top + area.Height / 2;
    _popup.Left = Left;
    _popup.Top = above ? Top - 450 : Top + Height + 10;
    _popup.Show();
    _popup.UpdateLayout();
    var h = _popup.ActualHeight; var w = _popup.ActualWidth;
    if (above && h > 0) _popup.Top = Top - h - 10; // separado un poco
    if (w > 0) _popup.Left = Left + (Width - w) / 2; // centrado con el widget
    ClampPopup();
    _popDx = _popup.Left - Left; _popDy = _popup.Top - Top;
    TickCountdown();
    _cdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _cdTimer.Tick += (_, _) => TickCountdown();
    _cdTimer.Start();
  }
  void TickCountdown() {
    _popup.Cd5.Text = L10n.T("left") + " " + Countdown(_last?.FiveHour);
    _popup.CdW.Text = L10n.T("left") + " " + Countdown(_last?.Weekly);
    _popup.CdCl5.Text = L10n.T("left") + " " + Countdown(_lastClaude?.FiveHour);
    _popup.CdClW.Text = L10n.T("left") + " " + Countdown(_lastClaude?.Weekly);
  }
  void ClampPopup() {
    var area = WorkAreaDip();
    _popup.UpdateLayout();
    var h = _popup.ActualHeight; var w = _popup.ActualWidth;
    if (h <= 0 || w <= 0) return;
    if (_popup.Top + h > area.Bottom) _popup.Top = area.Bottom - h;
    if (_popup.Top < area.Top) _popup.Top = area.Top;
    if (_popup.Left + w > area.Right) _popup.Left = area.Right - w;
    if (_popup.Left < area.Left) _popup.Left = area.Left;
  }
  Rect WorkAreaDip() {
    try {
      var src = PresentationSource.FromVisual(this);
      var toDev = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
      var fromDev = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
      var px = new System.Drawing.Point(
        (int)((Left + Width / 2) * toDev.M11), (int)((Top + Height / 2) * toDev.M22));
      var wa = WinForms.Screen.FromPoint(px).WorkingArea;
      return new Rect(wa.X * fromDev.M11, wa.Y * fromDev.M22, wa.Width * fromDev.M11, wa.Height * fromDev.M22);
    } catch { return SystemParameters.WorkArea; }
  }
}
