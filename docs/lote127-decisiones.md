# Lote 127 — Almacén de archivos: firma y fotos del comprobante (dictado de Luis, 2026-10-18)

## Qué se construyó
- **`StoredFile`** (sección (bg) de `update.sql`): la referencia de cada archivo —dueño polimórfico (EntityType + id, como `ContactPoint`), clase (`SIGNATURE`, `DELIVERY_PHOTO`, `DAMAGE_PHOTO`, `DOCUMENT`), nombre, tipo, tamaño, SHA-256, quién, cuándo y la llave en el almacén. EntityType `STORED_FILE` con auditoría.
- **`IFileStore` + `EncryptedDiskFileStore`**: disco local cifrado con AES-256-GCM y llave por compañía derivada (HMAC-SHA256) de la maestra `Files:MasterKey`; carpeta `{tenant}/{aaaa}/{mm}/{guid}.bin`; nunca se sirve por enlace directo. Mañana S3/Azure Blob detrás de la misma interfaz.
- **`StoredFileService`**: adjuntar (tipo real por los primeros bytes, tope `Files:MaxMegabytes`, nombre seguro, permiso de escritura del dueño y resolver de pertenencia), listar y servir (permiso de lectura del dueño), quitar en lógico. Para el comprobante: `orders.view` / `orders.deliver`.
- **Endpoints:** `POST /orders/{id}/delivery/files` (multipart), `GET /files/{publicId}` (inline, caché privada), `DELETE /files/{publicId}`; el comprobante (`GET /orders/{id}/delivery`) trae `files[]`.
- **Web:** panel del comprobante con «Firma y fotos» (miniaturas pedidas con la sesión como URL de objeto, PDF como enlace), «Agregar foto» y «Quitar» con `orders.deliver`.
- **Configuración:** `Files` en appsettings (llave vacía en el repositorio; llave de desarrollo en `appsettings.Development.json`); sin llave el API rechaza subidas con mensaje claro.

## Cómo se probó
- xunit: `StoredFileTests` (5: cifrado por compañía y rechazo con otra llave; tipo real, tamaño, clase, dueño, nombre seguro, baja lógica; permisos del dueño y aislamiento entre compañías; sin llave; nombres seguros). `DeliveryRegistrationTests` ajustado (dos POST con `orders.deliver`). Suite completa verde.
- vitest: `npm run check` verde (242 pruebas de órdenes).
- Playwright `lote127-anejos-comprobante.spec.ts` (escritorio y móvil) contra el API y SQL reales: 422 sin entrega, foto y firma por multipart, 400 con un texto disfrazado, el contenido con la sesión y 401 sin ella, miniaturas en la ficha, agregar y quitar.

## Decisiones a revisar
1. **Disco local cifrado** como primera implementación (sin dependencia de nube); la carpeta `Files:Root` y la llave maestra entran al respaldo. Cambiar a S3/Blob es implementar `IFileStore`.
2. **El permiso es el del dueño** (no un permiso nuevo de archivos): menos permisos que administrar y coherente con contactos y campos personalizados.
3. **La firma de la web sigue siendo la automática del usuario**; la del consignatario llegará de la app del chofer (P7) como archivo `SIGNATURE` y entonces el comprobante dirá «Firmada por quien recibió».
4. **Coordinación con la migración de Colavora:** esa sesión agregó `DeliveryProof.SignaturePath/PhotoPath` (rutas en disco, sección (bf)) para los comprobantes migrados. Recomendación: que la migración adjunte esas imágenes al almacén con `StoredFileService.AttachAsync` (quedan cifradas y se sirven igual) y esas dos columnas no se usen; si Luis prefiere conservarlas, el `GET /files` puede servir también esas rutas. Pendiente de decidir con Luis.
5. **Las fotos no se recomprimen en el servidor**: lo hará la app antes de subir; desde la web rige el tope de 10 MB.
