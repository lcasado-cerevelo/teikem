# Lote 135 — P11: API para los clientes (dictado de Luis, 2026-10-19)

## Qué se construyó
- **Conector `API`** (sección (bl) de `update.sql`: el CHECK admite `API`; tablas `IntegrationApiKey` e `IntegrationApiLog`). Cliente y plantilla opcionales; el cliente es obligatorio solo con alcances de transporte; una integración API solo admite alcances de API y las demás solo el de archivos.
- **Llaves:** `tk_`+20 hex y secreto `tks_…` de alta entropía; solo se guarda el hash SHA-256 (comparación en tiempo constante); hasta 10 activas; límite 1–6000/min; revocación inmediata.
- **Token:** `POST /api/v1/api-auth/token` (15 min; claims kind=API, scp, akid, cid) a nombre de la cuenta de servicio; `GET /api-auth/me`.
- **Lista cerrada de rutas por alcance** (`ApiAccessRules`) aplicada por `ApiKeyAccessMiddleware`: lo demás es 403; sin DELETE; con cliente, todo se limita a él por el filtro de datos (como el portal). Límite de uso en memoria con ventana de 60 s (429 + `Retry-After`, cabeceras `X-RateLimit-*`). Bitácora por llave con purga a 30 días.
- **Web:** conector en el formulario, acción «Llaves de API» (crear con secreto una sola vez, límite, revocar, bitácora), es/en.

## Cómo se probó
- xunit `ApiAccessRulesTests` (rutas por alcance, sin DELETE, secretos, límite, ventana) más las del área de integraciones.
- vitest de reglas del formulario (conector API) y las de Integraciones.
- Playwright `lote135-api-clientes.spec.ts` (escritorio y móvil) contra el API real: alcances, aislamiento entre dos clientes, 403/401/429, revocación, apagar integración, bitácora, y el recorrido de pantalla con capturas.

## Decisiones a revisar
1. **El límite de uso vive en memoria** (por instancia del servidor): con varias instancias el límite efectivo se multiplica. Suficiente hoy; si se escala horizontalmente, pasarlo a una tabla o caché compartida.
2. **Sin vencimiento por omisión de las llaves** (el API lo admite con `expiresAtUtc`; la pantalla aún no lo ofrece).
3. **Sin documento legal para llaves** (no son personas); la seguridad está en el secreto, los alcances, el cliente y el límite.
4. **Tokens de 15 min sin refresco:** se pide otro con la misma llave.
5. Las llamadas de almacén, ventas y compras de la API usan los mismos endpoints de la aplicación (y sus reglas); no hay una API paralela.
