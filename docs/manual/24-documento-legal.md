# 24 — Documento legal: política de privacidad y términos de uso

Lote 99 (portal de clientes, 2026-10-17). Hay **un solo documento** —«Política de privacidad y términos de uso»— con **una sola versión para toda la plataforma** (staff, clientes del portal y consignatarios). Está en español e inglés, en dos partes: la **Parte 1** (privacidad) y la **Parte 2** (términos de uso). **Es un borrador para revisión de un abogado antes de producción.**

## 1. Cómo funciona para el usuario
- Al entrar, si **no ha aceptado la versión vigente**, en lugar de la aplicación sale la pantalla del documento completo. **«Acepto…» se habilita solo cuando llega al final del texto** (hasta entonces dice «Desplácese hasta el final del texto para poder aceptar.»). «No acepto, salir» cierra la sesión.
- Sirve igual para **quienes ya tienen cuenta**: al pasar esto a producción, los usuarios existentes no tienen aceptación guardada y la verán en su siguiente ingreso.
- **Cuando el documento cambia** (nueva versión), todos vuelven a aceptarlo en su siguiente ingreso.
- El documento también se puede leer **sin sesión** en `/legal` (lo enlazarán las invitaciones y los portales).
- Mientras falte aceptar, el resto del API responde **403 `legal_acceptance_required`** («Debe aceptar la política de privacidad y los términos de uso para continuar.»). Siguen abiertos el inicio y cierre de sesión, `/me` y el propio documento.
- Quedan fuera por ahora las **sesiones de aparato** de la app de almacén (entran con PIN, sin pantalla web).

## 2. Quién aceptó y quién no
**Seguridad y auditoría → Aceptación legal** (permiso `admin.audit`): los usuarios de la compañía con su estado —**Aceptó** (con fecha), **Pendiente** o **Versión anterior (AAAA-MM-DD)**—, los pendientes primero. Búsqueda por nombre o correo, filtro por estado, ordenar columnas y exportar todo lo que dice el filtro. Cada aceptación guarda **versión, fecha, idioma, dirección IP y navegador**, y queda un evento en la bitácora de seguridad (`LOGIN`, etapa `legal_accepted`).

## 2b. Cookies y almacenamiento del navegador
Teikem **no usa cookies de publicidad ni de seguimiento**: guarda en el navegador (almacenamiento local) solo lo estrictamente necesario —sesión, la marca de la computadora, el idioma y la casilla «No confiar en esta computadora»—, que está exento de consentimiento, y **lo informa** en una sección de la política de privacidad («Cookies y almacenamiento del navegador», incluyendo que Cloudflare Turnstile puede guardar datos técnicos para comprobar que no es un robot). **Por eso no hay banner de cookies.** Si algún día se agrega analítica o publicidad, ese día sí hace falta pedir consentimiento antes (y poner el banner). Esta sección subió la versión a `2026-10-17.2`: todos aceptan de nuevo.

## 3. Endpoints
| Endpoint | Para qué | Quién |
|---|---|---|
| `GET /api/v1/legal/current?lang=es\|en` | El texto vigente (versión, título, secciones) | Público |
| `GET /api/v1/legal/status` | `currentVersion`, `required`, `accepted`, cuándo y qué versión aceptó | Sesión |
| `POST /api/v1/legal/accept` `{ version, lang }` | Acepta la versión vigente | Sesión |
| `GET /api/v1/legal/acceptances?status=accepted\|pending&q=&skip=&take=` | Quién aceptó y quién no | `admin.audit` |

## 4. Mensajes y errores
| Situación | Respuesta |
|---|---|
| Falta aceptar y se llama a cualquier otra parte del API | 403 `legal_acceptance_required`: `Debe aceptar la política de privacidad y los términos de uso para continuar.` |
| Se acepta una versión que ya no es la vigente | 409: `La versión del documento ya no es la vigente. Vuelva a leerlo.` |
| Aceptar dos veces la misma versión | No duplica; responde el estado |

## 5. Datos del operador y cambios del texto (para el dueño)
El texto lleva marcas que **hay que llenar antes de producción** en la configuración del API (`Legal:*`), y las que no se llenen salen tal cual entre corchetes:
- `Legal:OperatorName` — razón social del operador (hoy `[RAZÓN SOCIAL DEL OPERADOR]`).
- `Legal:OperatorAddress` — dirección postal.
- `Legal:ContactEmail` — correo de privacidad.
- `Legal:Jurisdiction` — ley y tribunales aplicables (por omisión «Puerto Rico»).
- `Legal:RequireAcceptance` — `true` por omisión; **solo se apaga en desarrollo**.

**Para cambiar el texto:** se edita `src/Teikem.Infrastructure/Legal/LegalText.cs` y se cambia `LegalRules.CurrentVersion` (la fecha) en `src/Teikem.Domain/Security/LegalAcceptance.cs`: eso hace que todos vuelvan a aceptar.

## Casos frecuentes
- **Un usuario dice que «no lo deja pasar»:** debe llegar al final del texto con el desplazamiento (en celular, deslizar hasta abajo del cuadro) para que se habilite el botón.
- **Un integrador o una app recibe 403 `legal_acceptance_required`:** el usuario de esa sesión debe entrar a la web y aceptar el documento.
- **Cambiamos el documento y todos quedaron pendientes:** es lo esperado; la lista de Aceptación legal muestra «Versión anterior».

**Versión `2026-10-21.1` (Lote 166/167):** el texto dice «envíos» donde decía «órdenes de entrega» (la orden de transporte pasó a llamarse envío). La versión subió, así que **todos los usuarios aceptan de nuevo** la próxima vez que entren.

**Dónde se ve estando dentro (2026-10-21):** al pie del menú lateral de la web hay, chico y apagado, los enlaces **Privacidad** y **Términos de uso** (abren el documento en otra pestaña, ya en la parte que corresponde), «© año Cerevelo LLC. Todos los derechos reservados.» y «Powered by Teikem». Con el menú contraído el pie se oculta; en el celular está en el menú. El dueño y el nombre del producto están en `web-app/src/app/AppLegalFooter.tsx`.

**En el portal del cliente (2026-10-21):** al final de **Ajustes**, en letra chica, **Privacidad** y **Términos de uso** (abren el documento solo para leer, ya en esa parte, con el botón **Volver**), los derechos reservados de Cerevelo LLC y «Powered by Teikem». No tiene botón de aceptar: la aceptación sigue siendo la pantalla obligatoria del primer ingreso.
