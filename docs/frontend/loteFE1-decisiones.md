# Lote F-E1 — Estación de escaneo, salida (2026-10-12)

Primer sub-lote de F-E (F-E1 escaneo, F-E2 Despacho con mapa, F-E3 Monitoreo; los dos últimos siguen `pending`). Pantalla Operación → **Estación de escaneo** (`/ops/scan`, `trips.scan`, módulo `LTL_GROUND`). Manual: [fe1-escaneo.md](../manual/frontend/fe1-escaneo.md); preguntas: sección «Lote F-E1» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`scan()`). Lote hermano: [loteFD3-decisiones.md](loteFD3-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/scan/`) |
|---|---|---|
| Pantalla | Cabecera con «Voz», casilla con foco + «Registrar», resultado grande con `aria-live`, «Ruta del día», filtros, medidor, lista de la sesión y pendientes | `ScanScreen.tsx`, `ScanResultBox.tsx`, `ScanPanels.tsx`, `scan.css` |
| Ventana de la orden | Ficha con `orders.view` y «Abrir»; sin permiso, lo ya sabido | `ScanOrderModal.tsx` |
| Reglas | Código, outcome → chip/voz/qué hacer, medidor, filtros, consulta de pendientes | `scanRules.ts` |
| Datos | `POST /scan/outbound` con `Idempotency-Key`, `GET /trips/unassigned-orders`, ficha de la orden | `scanApi.ts` |
| Voz y sesión | `speechSynthesis` Sí/No/Ya con preferencia en localStorage; lista de la sesión en memoria | `scanVoice.ts`, `scanSession.ts` |
| Kit | `IconScan`, `IconVolume`, `IconVolumeOff`; `useDispatchZones(…, enabled)`; sección «Escaneo» de `KIT.md` | `kernel/ui`, `features/drivers/api.ts` |

No se tocó el servidor ni el contrato (no hubo `openapi.json` ni `schema.d.ts` nuevos).

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `scanRules.test.ts` | código (vacío, espacios, 40/41), un color y nombre por outcome, voz, «qué hacer» por motivo, medidor, sesión (tope 200), buscadores, filtros, consulta, paridad es/en |
| `scanVoice.test.tsx` | sin soporte, preferencia (activa por omisión, recordada, almacenamiento bloqueado), idioma es-PR/en-US, cancelar la anterior |
| `ScanScreen.test.tsx` | cada outcome y motivo con su mensaje exacto, Enter y «Registrar», `Idempotency-Key` distinta, código vacío/largo sin llamar, 400 y 500, ráfaga en orden, fecha, sesión que sobrevive, ventana con y sin `orders.view`, filtros al servidor, medidor, permisos y módulo, voz |
| `e2e/loteFE1-escaneo.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal). Capturas `docs/manual/frontend/img/fe1-*.png` |

(Los conteos totales de vitest y de Playwright van en el reporte del lote.)

## Defectos encontrados y arreglados

1. **Pantalla: un código no encontrado desaparecía de la lista de la sesión al activar un filtro** (no tiene cliente ni zona que comparar). Ahora un código sin orden se ve siempre (prueba de regla y de pantalla; lo detectó el recorrido de Playwright).
2. **Pantalla: abrir una orden desde la estación y volver con «Atrás» perdía toda la sesión** (lista y medidor). La lista ahora vive en memoria por usuario y compañía y solo se pierde al recargar, cerrar sesión o cambiar de compañía (prueba de pantalla y de Playwright).
3. **Manual 05 §7** decía que sin fecha el escaneo usa «hoy UTC»; el servidor usa el día de la compañía. Corregido.
4. `useDispatchZones` no se podía apagar: sin `fleet.view` pedía zonas y daba 403. Ahora acepta `enabled` y la pantalla no lo pide sin el permiso.

Del servidor (precedencia de resultados, mensajes, búsqueda por orden/empaque/factura, 400 del código, rutas abiertas por zona y día) se recorrió por API y por pantalla y no mostró defectos.

## Diferencias con la maqueta (todas con su razón)

1. **Solo el modo «Salida»**: los modos Inbound y Procesamiento (cuadre COD) no existen en el servidor; se muestra la etiqueta «Salida» (no es un botón).
2. **Sin «Informe de faltantes»** (no existe el informe) y sin filtros «¿Con COD?» y «Ruta» (`unassigned-orders` no trae COD ni ruta; las órdenes ya asignadas salen de la lista).
3. **«Registrar» con la casilla vacía** no escanea «el primer pendiente» (era un atajo de la maqueta): avisa `Escanee o escriba un código.`
4. **Resultado grande** bajo la casilla (la maqueta solo daba luz verde y voz): muestra el mensaje exacto del servidor, el tipo de resultado y qué hacer.
5. **Feed con todos los resultados** (no solo los exitosos), con chip por tipo, mensaje y ruta; solo se filtra lo que se sabe de la orden.
6. **«Ruta del día»** (fecha del escaneo) y **buscador de la lista** con texto propio.
7. **Subtítulo de la pantalla** (`scan.sub`) habla solo de la salida; el del menú (`nav.scan.subtitle`, «inbound / outbound / COD») se dejó como en la maqueta.
8. Medidor con leyenda «órdenes asignadas en esta sesión» y «N» = órdenes distintas ASIGNADAS (la maqueta contaba toda la lista).
9. «Ver orden» muestra la ficha real (estatus del servidor, COD, ruta actual); «Abrir» solo con `orders.view`.

## Decisiones tomadas por default para presentar al dueño al final de P1

- Solo «Salida»; sin botones falsos de modos ni de faltantes (ver arriba).
- Foco automático; Enter o «Registrar»; las lecturas van **en fila una por una** (orden garantizado en ráfagas, a costa de no paralelizar); el foco vuelve a la casilla salvo que el usuario esté en otro campo o en un diálogo.
- `planDate` = hoy de la compañía (`tenantToday()`), editable; siempre se manda explícita.
- Voz: «Sí»/«No»/«Ya» (es) y «Yes»/«No»/«Already» (en), según `voice`; cancela la anterior; activa por omisión; preferencia en `teikem.scan.voice`; sin soporte, el botón queda deshabilitado con explicación. Un código demasiado largo también dice «No».
- Los avisos de código vacío o largo se validan en la pantalla con el MISMO texto del servidor y no llaman al API; un 400 real también se muestra con el texto del campo `code`.
- Lista de la sesión: solo navegador, máximo 200, por usuario y compañía; un `Idempotency-Key` por intento.
- Pendientes: se piden hasta 500 (`Take`) y el buscador filtra lo cargado; con más, avisa «Se muestran las primeras N de M». Los filtros CP y Pueblo esperan 300 ms antes de ir al servidor.
- Filtros que falta permiso o módulo para poblar (Zona sin `fleet.view`/CATALOG, o si el API da error; Cliente sin `clients.read`/CATALOG) se ocultan, sin errores.
- Los filtros acotan también el medidor y la lista de la sesión, comparando con lo que se sabía de la orden al escanear (de «Pendientes» o de la ficha con `orders.view`); una orden de la que no se sabe nada no pasa un filtro.
- Medidor: M = asignadas en la sesión + pendientes con el filtro; sin `trips.view` solo se muestra N.

## Qué no se pudo verificar

- **Lector físico**: se probó con el teclado (tecleo + Enter), como emula un lector; no con un lector real.
- **Voz real**: en las pruebas de componente se simula `speechSynthesis`; en Playwright solo se comprobó el interruptor y su memoria, no el audio.
- **Roles reales sin `trips.view`/`fleet.view`/`clients.read`/`orders.view`**: cubierto con pruebas de componente, no con Playwright.
- **Más de 500 pendientes y escaneos simultáneos desde varios puestos**: probado con respuestas simuladas.
