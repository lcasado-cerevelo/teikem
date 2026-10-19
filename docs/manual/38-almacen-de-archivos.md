# 38 — Almacén de archivos: firma, fotos y documentos

> Lote 127 (2026-10-18, dictado de Luis): la firma del que recibió, las fotos que se saquen en la aplicación móvil y los documentos se guardan como **archivos cifrados** en el servidor y en la base solo queda la **referencia**. Primer uso: el comprobante de entrega.

## 1. Qué hace
- Cada archivo pertenece a una **entidad dueña** (hoy el comprobante de entrega; mañana la orden, el vehículo, el chofer, un daño…), con su **clase**: `SIGNATURE` (firma), `DELIVERY_PHOTO` (foto de la entrega), `DAMAGE_PHOTO` (foto de un daño) o `DOCUMENT`.
- En la base (`StoredFile`) queda: dueño, clase, nombre, tipo, tamaño, **hash SHA-256**, quién lo subió, cuándo y la llave en el almacén. El **contenido** vive en el almacén de archivos del servidor (carpeta `Files:Root`, por compañía y mes), **cifrado con AES-256-GCM** con una llave derivada por compañía de la llave maestra (`Files:MasterKey`): un archivo copiado del disco no sirve de nada y la llave de una compañía no abre los de otra.
- **Nunca hay un enlace al disco:** el contenido se sirve por el API (`GET /api/v1/files/{publicId}`) con la sesión y el permiso de lectura del dueño. Sin sesión, 401.
- El **tipo real** se comprueba por el contenido (no por lo que dice el archivo): JPG, PNG, WEBP, GIF o PDF. Tope: `Files:MaxMegabytes` (10 por omisión). El nombre se limpia (sin carpetas ni caracteres raros) y toma la extensión del tipo real.
- **Quitar** es en lógico: deja de listarse y de servirse; el contenido no se borra del almacén.

## 2. Quién puede
El permiso lo decide el **dueño** del archivo. Para el comprobante de entrega: ver sus archivos exige `orders.view`; adjuntar y quitar, `orders.deliver`. Módulo `LTL_GROUND` (el de la orden).

## 3. Cómo se usa
- **Ficha de la orden → Comprobante de entrega → «Firma y fotos»:** miniaturas de las imágenes (se abren en grande) y enlaces de los PDF. Con `orders.deliver`: **Agregar foto** y **Quitar**.
- **App del chofer (P7):** subirá la firma del que recibió (`SIGNATURE`, y el comprobante pasará a «Firmada por quien recibió») y las fotos (`DELIVERY_PHOTO`) por los mismos endpoints.
- **Migración:** los comprobantes migrados pueden traer su firma y foto como archivos del almacén (se adjuntan con el servicio `StoredFileService.AttachAsync` al comprobante migrado).

| Método y ruta | Permiso | Qué hace |
|---|---|---|
| `POST /api/v1/orders/{publicId}/delivery/files` (multipart: `kind`, `file`) | `orders.deliver` | Adjunta al comprobante. 422 «La orden X no tiene registrada su entrega; registre la entrega antes de adjuntar archivos.» · 400 «Adjunte un archivo.» · «Solo se admiten imágenes (JPG, PNG, WEBP, GIF) y PDF.» · «El contenido del archivo no corresponde a su tipo.» · «La clase del archivo debe ser SIGNATURE, DELIVERY_PHOTO, DAMAGE_PHOTO o DOCUMENT.» · «El archivo no puede pasar de N MB.» · 422 «El almacén de archivos no está configurado (Files:MasterKey); avise a soporte.» |
| `GET /api/v1/orders/{publicId}/delivery` | `orders.view` | El comprobante trae `files[]` (publicId, kind, fileName, contentType, sizeBytes, createdAtUtc, uploadedByName). |
| `GET /api/v1/files/{publicId}` | lectura del dueño | El contenido, en línea (`Content-Disposition: inline`), caché privada de 5 min. 404 si no existe, no es de la compañía o se quitó. |
| `DELETE /api/v1/files/{publicId}` | escritura del dueño | Lo quita en lógico (204). |

## 4. Configuración (soporte)
`appsettings`: `"Files": { "Root": "App_Data/files", "MasterKey": "<32 bytes en base64>", "MaxMegabytes": 10 }`. En producción la llave maestra va en la configuración del servidor (variable `Files__MasterKey`), **nunca en el repositorio**, y hay que **respaldarla**: sin ella los archivos no se pueden leer. La carpeta `Files:Root` entra al respaldo del servidor junto con la base. Sin llave, el API arranca pero rechaza subidas con el mensaje de arriba.

## 5. Casos frecuentes
- **«El almacén de archivos no está configurado».** Falta `Files:MasterKey` (32 bytes en base64) en el servidor.
- **La miniatura no carga.** El contenido se pide con la sesión; si venció, vuelva a entrar. Si el archivo se quitó, desaparece de la lista.
- **Subí un .txt renombrado a .png.** No entra: el tipo se decide por el contenido.
