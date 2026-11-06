# Codex Usage Monitor

Widget para Windows que muestra el porcentaje restante de los límites de cinco horas y semanal de Codex y Claude Code.

## Uso

Descomprime el paquete completo en una carpeta estable y ejecuta `TaskbarWidget.exe`. Mantén juntos los archivos del paquete. No necesita permisos de administrador. El inicio con Windows es opcional y se puede desactivar desde el panel.

- **Codex:** instala el CLI oficial y usa «Iniciar sesión con ChatGPT». El navegador abre el flujo oficial. El widget no recoge contraseñas ni conserva tokens. «Cerrar sesión» afecta a la sesión compartida del CLI; un fallo se muestra y permite reintentar.
- **Claude:** pulsa «Conectar Claude». Se instala una barra de estado que entrega al widget únicamente métricas numéricas. Después abre Claude Code y envía un mensaje para obtener datos. Los límites dependen de los campos que proporcione tu versión y plan. Consulta [CLAUDE_SETUP.md](CLAUDE_SETUP.md) si ya utilizas una barra de estado.
- **Desconectar widget:** deja de aceptar datos de Claude y borra su caché de métricas. No cierra Claude Code ni borra credenciales.
- **Contexto:** opcional mediante «Mostrar contexto Claude». No se leen conversaciones. Los datos de Claude se actualizan mientras Claude Code los emite; con la sesión cerrada se indican como antiguos.

El widget conserva preferencias visuales y últimos porcentajes de uso en el perfil local de Windows. No envía telemetría propia. El proveedor puede cambiar su protocolo; los valores desconocidos se muestran con un guion.

## Compilar y comprobar

Requiere Windows y un SDK estable .NET 10 actualizado. Los proyectos no declaran paquetes NuGet de terceros. La publicación autocontenida incluye el runtime; debe regenerarse al actualizar sus parches.

```powershell
dotnet build CodexUsageMonitor.sln -c Release
dotnet run --project src/CodexProbe -c Release -- --selftest
dotnet run --project src/TaskbarWidget -c Release -- --selftest-all
pwsh -File scripts/publish.ps1
```

Las pruebas usan datos ficticios y no inician sesión, cierran cuentas ni leen credenciales. La prueba de interfaz termina con código 0 cuando pasa. `scripts/publish.ps1` genera los ZIP de código y aplicación, ejecuta pruebas y verifica secretos y rutas antes de empaquetar. Usa únicamente esos ZIP o los archivos versionados para publicar; no comprimas toda la carpeta de desarrollo.

El historial público empieza con una instantánea de la versión saneada. El historial de desarrollo anterior y sus artefactos no forman parte de esta distribución.

## Referencias

- [Protocolo Codex App Server](https://learn.chatgpt.com/docs/app-server).
- [Autenticación y almacén de credenciales Codex](https://learn.chatgpt.com/docs/auth).
- [Datos oficiales de la barra de estado de Claude Code](https://code.claude.com/docs/en/statusline).
- [Política de soporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
