# Conectar Claude Code

La integración recibe `rate_limits` y `context_window.used_percentage` de la barra de estado oficial. No consulta endpoints privados, no renueva tokens y no lee archivos de conversaciones o credenciales.

1. Descomprime el paquete completo en una ubicación estable.
2. Abre el widget y pulsa «Conectar Claude».
3. Abre o reinicia Claude Code y envía un mensaje. Los campos de límites pueden faltar hasta la primera respuesta o si el plan/versión no los proporciona.

El botón añade `statusLine` en la configuración de Claude solo si no existe, preservando las demás opciones. Si ya hay una barra de estado, no la sobrescribe. Configúrala para enviar también su JSON original de stdin a `CodexProbe.exe --claude-statusline`, manteniendo su salida actual. El comando debe apuntar a la copia del ejecutable incluida junto a TaskbarWidget.exe. No guardes ni registres ese JSON completo: contiene datos de la sesión que el widget descarta.

Ejemplo de entrada de configuración, sustituyendo la ruta por la de tu paquete:

```json
{
  "statusLine": {
    "type": "command",
    "command": "\"C:/Apps/CodexUsageMonitor/CodexProbe.exe\" --claude-statusline",
    "refreshInterval": 30
  }
}
```

Si has integrado manualmente una barra existente, el widget debe permitir activar la recepción sin reemplazarla: usa «Conectar Claude» y confirma la activación cuando avise de la barra existente. El JSON se procesa en memoria con un límite de tamaño y solo se conservan porcentajes, reinicios y fecha de recepción.

«Desconectar widget» desactiva la recepción y limpia las métricas bajo un bloqueo compartido con los callbacks. Los callbacks posteriores no vuelven a crear una sesión. El comando de la barra permanece instalado e imprime «Claude» mientras esté desconectado. Puedes quitarlo con `/statusline` en Claude Code. Para cerrar la sesión del proveedor utiliza el propio Claude Code.

Datos persistidos: `%LOCALAPPDATA%/CodexUsageMonitor/ClaudeBridge/usage.json`. No incluye correos, identificadores de cuenta, rutas de proyectos, modelos, conversaciones ni tokens. Si Claude Code deja de emitir datos, se muestra el último valor como antiguo. Las ventanas ya vencidas no se presentan como límites actuales.
