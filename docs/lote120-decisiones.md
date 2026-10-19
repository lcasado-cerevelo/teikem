# Lote 120 (P10) — configuración, perfil e indicadores del cliente (dictado de Luis, 2026-10-18)

## Qué se construyó
1. **Mi perfil** (`PortalProfileService`, `GET/PUT /portal/profile`): mi nombre (lo único que cambio; se guarda en la persona del portal y en la cuenta, y en las demás empresas a las que pertenezca la misma persona), mi correo y mi rol en solo lectura y los datos de mi empresa en solo lectura (nombre, código, razón social, identificación fiscal y dirección corporativa).
2. **Cambiar mi contraseña** desde la app (usa el `PUT /auth/password` que ya existía, que cierra las demás sesiones).
3. **Indicadores del cliente** (`PortalIndicatorsService`, `GET /portal/indicators?days=7|30|90`): creadas, entregadas, entregas a tiempo, horas promedio, intentos fallidos, en curso, y — según los módulos — saldo de facturas y vencidas, y dinero de COD por remesar. Tarjeta en el Inicio con selector de período.
4. **Configuración:** el «Ajustes» de la app ya reúne idioma, tema, seguridad, computadoras de confianza, Mis avisos (Lote 119) y Mis consignatarios; se le agregó el botón al perfil. La empresa no se edita desde el portal.
5. **Mensajes de contraseña en el idioma de la persona** (`LocalizedIdentityErrorDescriber`): hallazgo del recorrido, que mostró «Incorrect password.» en inglés bajo una pantalla en español (el FAQ lo documentaba como limitación). Ahora salen en es/en en todos los flujos de contraseña (cambio, olvidé mi contraseña, invitación, primer ingreso); el código del error no cambia.

## Cómo se probó
- **xunit:** `PortalProfileIndicatorsTests` (5: perfil y solo el nombre cambia, validaciones y sin acceso, indicadores con a tiempo / tarde / sin fecha / intentos / en curso solo del cliente, sin porcentaje cuando no hay fechas y días 7/30/90, facturas y COD con sus módulos) y `LocalizedIdentityErrorDescriberTests` (2). Suite completa verde.
- **jest** (app del cliente): perfil (datos, cambio de nombre, contraseña con sus validaciones) e indicadores (números, período, filas que no aplican).
- **Playwright** (`lote120-perfil-indicadores.spec.ts`, escritorio y móvil) contra el API y SQL Server reales: indicadores con tres órdenes reales (creadas 3, entregadas 1, en curso 2, 1 en tránsito, 400 con 15 días), el perfil (el nombre se cambia y el API lo confirma; el correo no cambia; nombre vacío 400) y la contraseña (validaciones de la pantalla, rechazo del servidor en español y cambio).

## Decisiones a revisar
1. **El correo no se cambia desde el portal** (es con el que se entra; cambiarlo exige verificarlo). Lo cambia el administrador.
2. **La empresa es de solo lectura** para el cliente; «configuración» queda como el Ajustes que ya existe.
3. **«A tiempo»** = entregada (día de la compañía) hasta el día de la fecha prometida; las órdenes sin fecha prometida no cuentan para el porcentaje.
4. **El nombre cambia en todas las empresas** de la misma persona (es una sola cuenta).
5. **Regla del dueño (2026-10-18):** todos los usuarios los controlan el administrador de la plataforma y el de la compañía. Este lote no agregó ninguna acción nueva de usuarios para el cliente (el cambio de nombre y de contraseña es de la propia persona); la revisión de lo existente y la lista única de usuarios quedaron anotadas en el plan.

## Qué quedó fuera de P10
Documentos adjuntos (falta decidir dónde se guardan los archivos) y el seguimiento en vivo (espera al rastreo de la app del chofer, lo último de todo).
