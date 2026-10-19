# Lote 134 — P11: Integraciones por SFTP y FTP, para cualquier cliente (dictado de Luis, 2026-10-19)

## Qué se construyó
- **Reglas puras** (`RemoteRules`, dominio): validación de la configuración con mensajes exactos; **huella SHA-256 del servidor** (normalización y comparación); qué archivos se toman (tipo y nombre, sin ocultos ni los modificados hace menos de 10 s; hasta 100 por corrida); rutas remotas seguras.
- **Clientes remotos** (`IRemoteFileClient`): SFTP con **SSH.NET 2026.0.0** (la 2024.2.0 y la 2025.1.0 traen vulnerabilidades conocidas; la 2026.0.0 restaura limpia) con la huella **fijada** (sin ella o distinta no se confía) y autenticación por contraseña o llave privada; FTP/FTPS con **FluentFTP 53** (TLS explícito con validación de certificado; hora exacta por MDTM porque el listado trae solo minutos y la hora del servidor sin zona). Listar, bajar y MOVER; nunca borra.
- **Ejecutor:** lista la carpeta, toma los archivos, los baja, los pasa por la plantilla (hash, importador en nombre de la cuenta de servicio, bandeja) y los mueve a «procesados» o «errores». **Probar conexión** genérico (`POST /{id}/test-connection`) para correo, SFTP y FTP: en SFTP devuelve la huella que presenta el servidor para fijarla.
- **Secretos:** contraseña o llave privada (con su frase) cifradas con `SecretProtector`; el DTO solo dice `hasRemoteSecret`.
- **SQL:** sección (bk) de `update.sql` — el CHECK de `Integration.ConnectorKind` admite `FTP`.
- **Web:** conectores SFTP y FTP en el formulario (sección «Servidor remoto»), «Probar conexión» para los tres tipos de conector.

## Cómo se probó
- xunit `RemoteRulesTests` (validación con mensajes exactos, huella, selección de archivos con el tiempo de asentamiento y el tope, rutas) y suite completa.
- vitest `remoteRules.test.ts` (formulario, secretos que no se mandan si no se escribieron, cuerpo del API) y las de Integraciones.
- **Playwright `lote134-sftp-ftp.spec.ts` contra servidores SFTP (ssh2) y FTP (ftp-srv) reales de prueba** en 127.0.0.1 (escritorio y móvil): sin huella no conecta y la prueba muestra la huella; una huella distinta se rechaza; con la huella fijada lista, lee solo el csv (el pdf se ignora por tipo), crea la orden (varias líneas por orden), **mueve** el archivo a «procesados» y el mismo contenido en otro archivo es «Repetido»; FTP sin cifrar avisa, lee, crea y mueve. Sin esos servidores (CI) la parte en vivo se omite (`REMOTE_TEST_SFTP_ROOT`, `REMOTE_TEST_FTP_ROOT`); las validaciones, la contraseña que no vuelve y la corrida contra un servidor inexistente siempre corren.
- **No se probó FTPS ni llave privada contra un servidor real** (los servidores de prueba son FTP simple y SFTP por contraseña); la lógica de llave PEM y FTPS es la de las librerías. Conviene una prueba de humo con el primer cliente real.

## Decisiones a revisar
1. **SFTP sin huella no conecta** (decisión de seguridad): obliga a un paso manual, pero evita hablar con un impostor.
2. **10 s de espera** para los archivos recién modificados (por si se están subiendo): un archivo puede tardar una corrida más.
3. **FTP sin cifrar se permite** con aviso (algunos clientes no tienen más); FTPS con certificado válido es lo recomendado.
4. **Descarga en memoria** (límite 2 MB por archivo, igual que el resto de las integraciones).
5. «**Vigilar carpeta**» del diseño = el horario de la integración (cada minuto como mínimo): no hay un observador en tiempo real.
