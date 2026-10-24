# Lote 207 — app-movil conectada a los datos de Teikem

## Qué se construyó
- **Capa de datos nueva** (`services/driverApi.ts`, `hooks/queries.ts`, `types/models.ts`): se eliminó el cliente de Colavora (`custom-api`, IDs numéricos, piezas, comentarios, filtros de oficina). Una consulta, `useDriverWork`, baja los manifiestos y envíos de **todas las compañías** del chofer (un token por compañía), cada uno marcado con su compañía; si una compañía no responde, las demás se muestran y se avisa cuál falló.
- **Inicio**: Recogidas para hoy, Manifiesto de recogida, Entregas para hoy, Manifiesto de entrega (recogidas primero; un botón sin contenido no se muestra). Hoy el servidor solo entrega manifiestos de entrega: las recogidas aparecerán cuando se construya el viaje con tipo.
- **Aceptar manifiesto** (pantalla nueva `AcceptManifest`): el chofer marca los envíos que no acepta y escoge el motivo de la lista de la compañía; confirma y sale la ruta. Requiere señal (la oficina lo ve de inmediato).
- **Entregar / no entregar**: quién recibió, nota, foto y firma (opcionales), GPS del teléfono; **no entregado** con motivo de «Razón de fallo». Todo pasa por la cola sin señal (SQLite) con el `tenantId` de cada envío; el servidor es idempotente.
- **Búsqueda** por orden, guía, consignatario, dirección, ciudad, notas, manifiesto y por **compañía (nombre o número)**.
- Quitado: tabs Piezas y Comentarios, filtros por remitente/fecha/ciudad, cambio de compañía, chequeo de versión de Colavora (`common/version` no existe en Teikem).

## Cómo se probó
`npx tsc --noEmit` y `expo export --platform web` (arman). **No se corrió contra el API real ni en un teléfono**: falta una pasada de humo con un chofer de prueba (login → aceptar → entregar → no entregar).

## Decisiones a revisar
- Cobro COD: la app muestra el monto a cobrar, pero no registra el cobro (nota «se registra con la oficina»).
- Firma y foto son opcionales (en Colavora eran obligatorias por cliente).
- Mapa: sigue geocodificando la dirección con Nominatim (OpenStreetMap), igual que Colavora; podría usar las coordenadas de la parada si el servidor las expone.
- Pendientes de las siguientes partes: recogidas (viaje con tipo), GPS de ruta, avisos, aviso de privacidad del chofer.
