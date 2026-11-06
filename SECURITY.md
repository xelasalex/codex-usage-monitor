# Seguridad y publicación

- Codex es propietario de su sesión. Se validan destino HTTPS de login, identificador y resultado de la notificación. El cierre de sesión debe completarse y confirmarse con `account/read`.
- Los errores remotos no se incluyen en excepciones ni salida de diagnóstico. No se imprimen URLs OAuth, correos o respuestas completas de cuenta.
- Las credenciales de Claude nunca se leen, escriben ni eliminan. La conexión utiliza datos oficiales de su barra de estado y guarda únicamente métricas permitidas. Desconectar el widget no desconecta el CLI.
- Las llamadas Codex usan stdin/stdout. Las escrituras se serializan, las respuestas tienen límite de tamaño, stderr se consume sin registrarlo y se termina el árbol de procesos propiedad del widget al cerrar.
- Solo se resuelven CLI desde entradas absolutas del PATH, respetando su orden. Para wrappers CMD se usa el intérprete del sistema sin AutoRun y se rechazan caracteres de expansión de comandos.
- No ejecutar el widget como administrador. Instalar el paquete en una carpeta del usuario y mantener el CLI y el runtime actualizados.
- Codex puede persistir credenciales en archivo o en el almacén del sistema. Si necesitas exigir el almacén protegido, configura `cli_auth_credentials_store = "keyring"` en Codex; `auto` puede volver a archivo. Este proyecto no cambia silenciosamente esa configuración compartida.
- Los proyectos bloquean la serialización insegura BinaryFormatter. Las publicaciones Release no incluyen símbolos personales y normalizan las rutas de compilación.

Antes de cada publicación ejecuta `scripts/publish.ps1`. El control verifica los archivos seleccionados, artefactos, historial y metadatos Git; no imprime los valores encontrados. Los tests de seguridad cubren errores de logout, destinos OAuth maliciosos, correlación del login, mensajes sensibles, cancelación, concurrencia y desconexión de Claude.

Nunca adjuntes carpetas `.git`, `.superpowers`, `bin`, `obj`, archivos de sesión, `.env`, logs o volcados a una incidencia o descarga. El repositorio limpio no contiene las copias privadas del desarrollo anterior. Usa una identidad Git pública o noreply para nuevos commits. Una detección por patrones no garantiza que todos los secretos posibles sean reconocibles.
