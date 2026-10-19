# Lote 132 — P11: Integraciones de entrada (carpeta y subida manual) con bandeja (dictado de Luis, 2026-10-19)

## Qué se construyó
- **Modelo** (sección (bj) de `update.sql`): `Integration` (cliente, plantilla, conector, horario, alcances, cuenta de servicio, a quién avisar, retención, candado de corrida), `IntegrationRun`, `IntegrationFileLog` (hash SHA-256 único por integración cuando se procesó) e `IntegrationInboxItem` (motivo + datos crudos); `NotificationRule.ScopeKey`; módulo `INTEGRATIONS`; 7 permisos `integrations.*` (con **un permiso por familia** para crear) a TenantAdmin y a los roles ya clonados.
- **Cuenta de servicio por integración** (usuario `SERVICE` sin contraseña que el login rechaza + rol «Integración: nombre» con los permisos de sus alcances). **Sin escalada de privilegios:** quien crea solo da alcances de familias que puede crear, de módulos que la compañía tiene y cuyos permisos posee (`IntegrationScopes.Validate`).
- **Ejecutor** (`IntegrationRunner`): carpeta de entrada (raíz configurable, nombre validado, nunca sale de la raíz; los archivos se mueven a `procesados/` o `errores/`, jamás se borran) y subida manual; una corrida a la vez (candado atómico); cada archivo pasa por el importador de órdenes **en nombre de la cuenta de servicio** (validar → confirmar las buenas); lo demás va a la bandeja con el motivo exacto de la vista previa y las filas crudas. Trabajo programado `INTEGRATION_RUN_DUE` (cada minuto) que corre las integraciones por horario y purga el detalle viejo de la bandeja.
- **Bandeja** (`IntegrationInboxService`): listar, corregir celdas, reprocesar (uno o todos), descartar con nota, exportar CSV; la purga deja el motivo.
- **Avisos:** el acuse es un aviso del sistema (campanita y correo) a quienes se definen en la integración, con «Incluirme»: cada integración administra una regla de aviso con `ScopeKey` y el procesador de avisos solo dispara esa regla con los avisos de esa integración.
- **Web:** Sistema → Integraciones / API (lista, alta/edición, historial, subir archivo, bandeja con corregir/descartar), es/en, claro/oscuro, 360 px.

## Cómo se probó
- xunit `IntegrationRulesTests` (alcances sin escalada, mensajes exactos, carpeta, horario, nombres de la cuenta de servicio, datos crudos y CSV, decodificación, motivo, permisos en el catálogo) y ajuste de los tres conteos de permisos (108). Suite completa verde (4.626).
- vitest `integrationRules.test.ts` y `IntegrationsScreen.test.tsx`; `npm run check` verde (con el fallo intermitente conocido de workers de vitest).
- Playwright `lote132-integraciones.spec.ts` (escritorio y móvil) contra API y SQL reales: validaciones del alta, cuenta de servicio (no inicia sesión; rol con los permisos del alcance), carpeta con la plantilla de varias líneas (2 creadas, 2 a la bandeja, archivo movido), el mismo archivo (Repetido), aviso del sistema, corregir y reprocesar, reprocesar de nuevo (409), descartar, CSV, apagar/prender (422), y la pantalla completa.

## Decisiones a revisar
1. **Solo CSV/texto** por ahora (Excel .xlsx llega con el mapeo del lote 137); la codificación cae a Latin-1 si no es UTF-8.
2. **Una orden por ítem de la bandeja** (con todas sus líneas); un archivo ilegible queda como un ítem sin datos.
3. **El «hash» de un reproceso no cuenta** para el «ya procesado» del archivo (resultado `REPROCESSED`).
4. **Notificación solo si la corrida hizo algo** (creó, rechazó o falló); una corrida vacía no avisa.
5. **El usuario de servicio aparece** en la lista de usuarios de la compañía (clase Servicio); no inicia sesión. Si Luis prefiere ocultarlo en esa lista, es un filtro.
6. **La carpeta es del servidor** (el API la lee): para clientes que dejan archivos en SFTP/correo, llegan los lotes 133 y 134.

## Sigue en P11
133 correo (remitente original, adjuntos por nombre/tipo, campos del cuerpo) · 134 SFTP/FTP · 135 llaves de API · 136 webhooks · 137 mapeo JSON/XML y Excel (ver `docs/p11-diseno-integraciones.md`).
