namespace TaskbarWidget;
static class L10n {
  public static string Lang = "es";
  static readonly Dictionary<string, string[]> M = new() {
    ["retry"] = ["Reintentar", "Retry"],
    ["restart"] = ["Reiniciar", "Restart"],
    ["login"] = ["Iniciar sesión", "Sign in"],
    ["logout"] = ["Cerrar sesión", "Sign out"],
    ["exit"] = ["Salir", "Exit"],
    ["open"] = ["Abrir", "Open"],
    ["autostart"] = ["Iniciar con Windows", "Start with Windows"],
    ["lock"] = ["Bloquear posición", "Lock position"],
    ["overlay"] = ["Superponer a todo", "Always on top"],
    ["size"] = ["Tamaño", "Size"],
    ["poll"] = ["Actualizar cada", "Refresh every"],
    ["delay"] = ["Retardo arranque (s)", "Startup delay (s)"],
    ["lang"] = ["Idioma", "Language"],
    ["nosession"] = ["Sin sesión", "No session"],
    ["offline"] = ["Desconectado — último valor", "Offline — last value"],
    ["connecting"] = ["Conectando en {0}s…", "Connecting in {0}s…"],
    ["nocodex"] = ["Codex CLI no instalado (npm i -g @openai/codex)", "Codex CLI not installed (npm i -g @openai/codex)"],
    ["loginprompt"] = ["Iniciar sesión con ChatGPT", "Sign in with ChatGPT"],
    ["remaining"] = ["restante", "remaining"],
    ["resets"] = ["reinicio", "resets"],
    ["unknown"] = ["desconocido", "unknown"],
    ["restarting"] = ["reiniciando…", "resetting…"],
    ["left"] = ["Restan", "Left"],
    ["resetdone"] = ["límite reiniciado", "limit reset"],
    ["sound"] = ["Sonido", "Sound"],
    ["test"] = ["Probar", "Test"],
    ["claudelogin"] = ["Conectar Claude", "Connect Claude"],
    ["disconnect"] = ["Desconectar widget", "Disconnect widget"],
    ["disconnected"] = ["Desconectado", "Disconnected"],
    ["connected"] = ["Conectado", "Connected"],
    ["showcontext"] = ["Mostrar contexto Claude", "Show Claude context"],
    ["logoutfailed"] = ["No se pudo cerrar la sesión. Reintenta.", "Sign out failed. Please retry."],
    ["loginfailed"] = ["Inicio de sesión no completado.", "Sign in was not completed."],
    ["disconnectfailed"] = ["No se pudo desconectar. Reintenta.", "Disconnect failed. Please retry."],
    ["claudewaiting"] = ["Abre Claude Code y envía un mensaje para actualizar.", "Open Claude Code and send a message to update."],
    ["claudeexisting"] = ["Tu barra de estado se conservará. ¿Ya has integrado el widget siguiendo CLAUDE_SETUP.md y quieres activar la recepción?", "Your status line will be preserved. Have you integrated the widget using CLAUDE_SETUP.md and want to enable reception?"],
    ["claudesetuperror"] = ["No se pudo configurar Claude. Usa el paquete completo.", "Claude setup failed. Use the complete package."],
    ["noclaude"] = ["Claude Code CLI no instalado (npm i -g @anthropic-ai/claude-code)", "Claude Code CLI not installed (npm i -g @anthropic-ai/claude-code)"],
  };
  public static string T(string k, params object?[] a) {
    if (!M.TryGetValue(k, out var v)) return k;
    var s = Lang == "en" ? v[1] : v[0];
    return a.Length == 0 ? s : string.Format(s, a);
  }
}
