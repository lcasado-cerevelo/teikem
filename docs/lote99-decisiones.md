# Lote 99 — Documento legal único (portal de clientes, 3) (2026-10-17)

Primer lote de seguridad del portal ([p10-portal-diseno.md](p10-portal-diseno.md)): **una sola política de privacidad y términos de uso** para toda la plataforma, con su aceptación obligatoria. Luis pidió: un solo documento (no dos pestañas), una sola versión para todos, hecho completo en la aplicación y que él lo revisa después con un abogado; los usuarios que ya existen deben aceptarlo en cuanto se pase a producción, con registro de quién aceptó y quién no, y volver a aceptar cuando cambie.

## Qué se construyó
- **Documento** en español e inglés (`Infrastructure/Legal/LegalText.cs`): Parte 1 privacidad (12 secciones: datos, usos, responsables, terceros, avisos por correo y texto con baja, retención, seguridad, derechos…) y Parte 2 términos (11 secciones: cuenta y computadoras de confianza, uso aceptable, datos de terceros, órdenes creadas por consignatarios, responsabilidad, ley aplicable…). Datos del operador desde `Legal:*`; versión vigente `LegalRules.CurrentVersion` (fecha).
- **Aceptación** (`LegalAcceptance`, `update.sql` (ah)): usuario, versión, idioma, IP, navegador y fecha; tabla global sin `TenantId`. Los usuarios existentes no tienen fila → pendientes.
- **Exigencia en el servidor** (`LegalAcceptanceMiddleware`): 403 `legal_acceptance_required` para todo el API menos sesión, `/me` y `/legal/*`; se apaga solo con `Legal:RequireAcceptance=false` (desarrollo).
- **API** `/api/v1/legal`: `current` (público), `status`, `accept` (409 si la versión ya no es la vigente), `acceptances` (quién aceptó; `admin.audit`; filtros y paginado; solo usuarios de la compañía activa; pendientes primero).
- **Web:** compuerta en `RequireAuth` con la pantalla de aceptación (botón solo al final del texto, «No acepto, salir»), página pública `/legal`, pestaña «Aceptación legal» en Seguridad y auditoría (con exportar completo), textos es/en, openapi y cliente generado.
- Manual 24, manual de pantallas L99 con capturas, FAQ, diseño del portal actualizado con todo lo decidido.

## Cómo se probó
- xunit `LegalTests` (6): documento completo en los dos idiomas con datos del operador y sin marcas sueltas, mismas secciones en es/en, rutas exentas, aceptar guarda versión/IP/navegador y no duplica, versión vieja = 409 y una aceptación anterior no cuenta, la exigencia apagada, y la lista (pendientes primero, solo la compañía activa, filtros). Suite completa.
- vitest (5 + 1 ajustada): reglas del scroll, compuerta (botón deshabilitado hasta el final, aceptar deja pasar, «No acepto» cierra sesión, quien aceptó entra directo) y lista. Playwright contra el API real, escritorio y móvil: usuario nuevo ve el documento, el botón se habilita al final, acepta, al recargar no se repite, y el administrador lo ve como «Aceptó».
- Contra SQL real: `db-init` aplicó (ah); sin aceptar, `/orders` responde 403 `legal_acceptance_required`; versión vieja 409; aceptar habilita; la lista responde.

## Decisiones a revisar con Luis
1. **El texto es un borrador** y hay que **llenar `Legal:OperatorName/OperatorAddress/ContactEmail/Jurisdiction`** antes de producción; el abogado debe revisar sobre todo privacidad (derechos, retención, brecha) y el consentimiento de SMS.
2. **Las sesiones de aparato** (app de almacén con PIN) quedan **fuera** de la exigencia por ahora: no tienen pantalla web para aceptar. Si hay que exigírselo a esos usuarios, se pide al enrolar el aparato.
3. Una aceptación vale por **versión**; cambiar el texto sin cambiar `CurrentVersion` no obliga a re-aceptar.
4. La aceptación de quien entra por **portal de clientes y consignatarios** usará la misma tabla y la misma pantalla (lotes 101 y 105).
5. Lo pedido para los accesos del portal (computadoras de confianza a 30 días, candado antifuerza bruta común, invitación que vence, avisos del consignatario con su aceptación, portal instalable) quedó **escrito en el diseño** y entra en los lotes 100–105.
