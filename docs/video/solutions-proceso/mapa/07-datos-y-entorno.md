# Datos y entorno para grabar el video de Advance Solutions

Tramo 7 del mapa (solo lectura, 2026-11-12). Todo lo de la base sale de consultas `SELECT` con `TenantId = 2` contra `localhost,1433`, base `Teikem`. No se arrancó ningún servidor ni se tocó ninguna base. Los tiempos de respaldo, restauración e instantánea son **estimaciones** (no se midieron, porque medirlos exige escribir).

---

## 1. Estado de Advance Solutions en la base local

Servidor: SQL Server 2022 **Developer Edition** (16.0.1210.3). Developer incluye las funciones de Enterprise, entre ellas las **instantáneas de base de datos**.
Archivos de `Teikem`: datos **136 MB**, log **2 760 MB** (recuperación FULL, log reservado). Discos libres: **C: 1 020 GB** (ahí viven los archivos de SQL Server), **F: 755 GB**, B: 1,7 GB.
Los archivos subidos (fotos, firmas) viven fuera de la base, en `src/Teikem.Api/App_Data/files` (**238 MB** de todas las compañías). `StoredFile` de Solutions tiene **65 imágenes**.

| Concepto | Valor hoy | Consulta (resumida) |
|---|---|---|
| Usuarios de la compañía | **2** activos: 1 `TenantAdmin` y 1 `Sales Supervisor`. Ambos con `TwoFactorEnabled = 1` y **0** filas en `UserMfaFactor` | `UserTenant` + `AspNetUsers` + `UserRole` |
| Roles activos | TenantAdmin (159 permisos), Dispatcher, Billing, WarehouseOperator, Driver, ReadOnly, Sales Supervisor. **Faltan** «Ventas» y «Oficina de ventas» (los crea el script 03) | `Role WHERE TenantId=2` |
| Almacén | 1: `ALM-SOLUTIONS` «Almacen Solutions», `IsMain = 0`, modo de recibo lookup 311 | `Warehouse` |
| Zonas y posiciones | 4 zonas: STG «Recepción» (0 posiciones), **PCK «Picking» (79)**, RSV «Reserva» (0), PISO «Piso» (0). La posición **PISO** está dentro de la zona PCK | `WarehouseZone`, `WarehouseBin` |
| Productos activos | **42**; 39 con marca; **34 con foto**; 40 con costo de compra; 42 con precio de venta (de 0,95 a 28,99); 11 con categoría de impuesto; seguimiento `NONE` en los 42; 0 con `QuickBooksSentAt` | `Product` |
| Marcas | PRODIGY 7, PADIMONT 4, GLOBAL 6, AMERICAN RED CROSS 9, MEDPRIDE 9, YARDLEY 1, ABELLE 1, DEPOT 2, sin marca 3 | `GROUP BY Brand` |
| Existencia | 38 filas de `StockBalance`, todas en la posición **PISO**, **69 900 unidades**; 4 productos activos sin existencia | `StockBalance` |
| Proveedores | **0** | `Supplier` |
| Clientes | **701** activos (estatus `ACTIVE`). Término de pago: CHEQUE 374, NET30 202, NET15 40, NET45 21, ACH 21, CASH 20, CONSIGNMENT 11, NET20 3, sin término 9; 0 con `QuickBooksSentAt` | `Client` |
| Listas de precios | 1: «Lista general (precios de QuickBooks, 2026-11-10)», desde 2026-11-11, sin fin, publicada, para todos, **39** ítems | `PriceList` |
| Ofertas | 1: «OFERTA DEL DIA», desde 2026-11-10, sin fin, publicada, para todos, 1 ítem: el kit `KIT-001` «kIT DE mONITOR gRATIS» a 8,50, sin foto de oferta | `Offer`, `OfferItem` |
| Kit | `KIT-001`: `IsOfferKit = 1`, 6 × `52800` (24 100 disponibles) y 1 × `070120` (2 332 disponibles). Misma composición que OFERTA1 (script 02) pero con nombre de prueba | `ProductKitComponent` |
| Órdenes de venta | 2: OV-00001 (Cancelada, 0 líneas) y OV-00002 (Solicitada, retiro, 1 línea, con firma) | `SalesOrder` |
| Órdenes de compra | **0** | `PurchaseOrder` |
| Facturas | **0** | `Invoice` |
| Tareas de almacén / movimientos | 0 tareas; 38 movimientos de inventario (el saldo inicial) | `WarehouseTask`, `InventoryTransaction` |
| Impuestos | 1 tasa; precios sin impuesto incluido, redondeo por línea, tasa por omisión **IVU11-5** | `TaxRate`, `TenantTaxSetting` |
| Módulos | ANALYTICS, CATALOG, CUSTOM_FIELDS, INTEGRATIONS, PURCHASING, SALES_ORDERS, SYSTEM, TAX, WMS_LOTSERIAL encendidos; **BILLING = 0** | `TenantModule` |
| Integraciones de Solutions | 0 | `Integration` |

**Ajustes de la compañía (tabla `Tenant`, fila 2):**

| Ajuste | Valor | Nota |
|---|---|---|
| MFA exigido (`MfaRequired`) | **0 (no)** | Bien para grabar. Ventana AAL2: 30 min |
| Fotos de productos (`ProductPhotosEnabled`) | **1** | Lote 240 |
| Requisito de recibo / despacho | `FREE` / `FREE` | Sin pasos obligatorios |
| Rotación | FEFO, aplicación `SUGGEST`, sin lote FIFO; valoración `AVERAGE` | |
| Base de facturación | `ON_DISPATCH`, vence a 30 días | |
| Envío | cargo NULL, gratis desde NULL, sin IVU | El script 08 pone $12,00 gratis desde $120,00; **no aplicado** |
| QuickBooks | cuentas de cobrar, ventas, costo, inventario e impuesto: todas NULL; ítems de IVU y envío NULL; términos NULL | El script 08 pone los nombres de los términos; **no aplicado** |
| Kits ensamblados | `UsesAssembledKits = 0` | |
| Moneda / idioma | USD / es | |

**Estructura de los lotes 238 a 240:** la base **ya los tiene**. Existe la tabla `UserPreference` (lote 238, `Diseño/logistica-db-update.sql:7843`) con 0 filas para Solutions, y la columna `Tenant.ProductPhotosEnabled` (lote 240, `Diseño/logistica-db-update.sql:7857-7861`) con valor 1. Esas secciones son las últimas del archivo (7 862 líneas), así que **no falta ningún `db-update`** si la copia se hace desde la base local actual. Si se partiera de otra base, se comprueba con `COL_LENGTH('dbo.Tenant','ProductPhotosEnabled')` y `OBJECT_ID('dbo.UserPreference')`.

---

## 2. Lo que falta para poder grabar las seis escenas

| Necesidad | Estado hoy | Cómo se arregla | Con qué |
|---|---|---|---|
| **Proveedores** (escena 2: el importador de QuickBooks **bloquea** la orden si el proveedor no existe, `docs/lote239-decisiones.md`) | 0 proveedores. `Solutions Purchases.CSV` trae 58 facturas (Bill), 24 recibos (Item Receipt) y 1 crédito; `Solutions Vendors.csv` tiene 40 líneas | Cargar los proveedores de `suppliers.includeNames` de `docs/migracion/import.solutions.json` con `dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json --update`, **apuntando a la copia** (`ConnectionStrings__Teikem`) y primero con `--dry-run`. Alternativa: darlos de alta a mano en la pantalla de Proveedores (`/warehouse/suppliers`, permiso `suppliers.view`, `web-app/src/app/routes.tsx:231-235`) con el nombre exacto de QuickBooks | `import-legacy` (`docs/migracion/README.md`) o pantalla |
| **Documentos del archivo de compras** | De 19 documentos del archivo real, 9 salen listos, 2 bloqueados (`101010` y `53310/53350` no existen en Teikem) y 8 se omiten (7 recibos y 1 crédito) (`docs/lote239-decisiones.md:18`) | Elegir **uno o dos de los 9 listos** y dejar un CSV recortado con solo esas filas (archivo nuevo; no se edita el original) | `F:/Download/Solutions Purchases.CSV` |
| **Módulo BILLING** (escena 6) | Apagado. La exportación QuickBooks saca **facturas emitidas** (Emitida, Vencida o Pagada) que nacen de una **corrida de facturación** generada y aprobada (`docs/manual/49-quickbooks.md:15-19`, `docs/manual/19-facturacion.md:9`). Con 0 facturas la pestaña está vacía y «Exportar» responde que no hay nada por pasar (`QuickBooksService.cs:103`) | Encender el módulo en la copia (Sistema → Ajustes → Módulos, o el helper `enableModule`, `web-app/e2e/staffHelpers.ts:134`, que pide la contraseña), generar y **aprobar** una corrida para la orden de venta despachada (aprobar exige AAL2: volver a escribir la contraseña, `docs/manual/19-facturacion.md:17`). Se hace antes de la toma de la escena 6 o dentro de ella | Pantalla Contabilidad → Facturación (`billing.generate`, `billing.approve`) |
| **Ajustes de QuickBooks** (escena 6) | Cuentas e ítems NULL: el IIF sale con los valores por omisión; un término sin nombre sale sin término y la pestaña lo avisa (`es.json:8747-8774`) | Script `08-ajustes-envio-quickbooks.sql` en la **copia** (simulado y luego `@Aplicar = 1`): envío $12 gratis desde $120 y nombres de términos | Script 08 |
| **Clientes y productos «nuevos» en QuickBooks** | 0 clientes y 0 productos con `QuickBooksSentAt`: la primera exportación los marcaría como «Nuevo en QuickBooks» y preguntaría «Incluir los clientes y productos nuevos» (`es.json:10476,10496`) | Script `10-quickbooks-ya-mandados.sql` en la copia, para que la exportación lleve solo la factura de la demostración | Script 10 |
| **Fotos de productos** (escena 1) | 34 de 42 con foto; faltan 8. Los archivos están en `App_Data/files`, la base solo guarda la llave | Si las 34 bastan, nada. Para completar: `13-fotos-productos.ps1` (34 filas en el CSV; carpeta `F:/Download/FotosSolutions`, 30 archivos) **contra el API de grabación**, primero sin `-Aplicar` (simula). Usa `POST /api/v1/products/{id}/image`, permiso `inventory.manage` | Script 13 |
| **Lista de precios y oferta con kit vigentes** | Hay lista general publicada (39 ítems) y la oferta publicada «OFERTA DEL DIA» con `KIT-001`, vigentes y sin fecha de fin | Lista. Opcional: renombrar el kit y la oferta (el nombre `kIT DE mONITOR gRATIS` se ve mal) en Precios y ofertas, o correr `02-kit-oferta1.sql` en la copia (OFERTA1: 6 × 52800 + 1 × 070120 a $41,94) y desactivar la oferta de prueba | Pantalla o script 02 |
| **Existencias** | 69 900 unidades en 38 productos, todas en PISO; los componentes del kit tienen 24 100 y 2 332 | Alcanzan para vender y empacar. Para la escena 3 (recibo): la zona STG «Recepción» tiene **0 posiciones** y `DefaultReceivingBinId` es NULL, así que un recibo «con acomodo» no tiene destino claro. `scripts/demo/solutions-nueva-posicion.sql` busca el almacén `ALM-SOL` (aquí se llama `ALM-SOLUTIONS`): **no sirve tal cual**. Crear la posición por la pantalla de Almacenes o recibir directo a PISO | Pantalla de Almacén |
| **Usuarios de demostración con contraseña conocida y sin MFA** | Los 2 usuarios actuales son personas reales, con `TwoFactorEnabled = 1`, sin factores y contraseña desconocida | Crear usuarios nuevos en la copia con la contraseña escrita (el API acepta `Password` en `UserCreateRequest`, `UserAdminService.cs:108-109`) y el API de grabación con `Auth__Onboarding__Enabled=false`. Ver sección 5 | Pantalla Sistema → Roles y usuarios o `POST /api/v1/users` |
| **Roles Ventas y Oficina de ventas** | No existen en Solutions | `03-roles.sql` en la copia (crea «Supervisor de ventas» y «Oficina de ventas»; asegura permisos a «Ventas» y Admin). La base ya trae un rol «Sales Supervisor» en inglés: puede avisar de duplicado | Script 03 |
| **db-update pendiente** | **Ninguno** si la copia sale de la base local actual | — | — |
| **Preferencias del asistente de ventas** | `UserPreference` vacía: cada usuario nuevo entra con el modo por omisión | Decidir qué modo se ve primero y fijarlo en pantalla para cada usuario demo (lote 238) | Pantalla |
| **Reglas de aviso con correo** | El script 09 crea reglas con correo; con Brevo vacío no sale nada, pero no hace falta | **No correr** el script 09 en la copia | — |

---

## 3. Con qué base se graba

Regla: nunca grabar sobre `Teikem` (desarrollo de Luis), ni TEST, ni producción. `scripts/api-para-pruebas.ps1` usa por omisión `Teikem_e2e`, pero esa base solo tiene Advance Logistics y Advance Depot; no sirve para Solutions.

| Opción | Cómo | Tiempo (estimado) | Espacio | Riesgo | Datos reales a la vista |
|---|---|---|---|---|---|
| **A. Copia por respaldo y restauración** a `Teikem_video` | `BACKUP DATABASE Teikem ... WITH COPY_ONLY, COMPRESSION` y `RESTORE DATABASE Teikem_video ... WITH MOVE` | **30 a 90 s** en total (datos de solo 136 MB; el log de 2,7 GB se reserva al restaurar) | En C: unos 3 GB (se encoge el log después) más el `.bak` de decenas de MB | Bajo: `COPY_ONLY` no afecta los respaldos de Luis. Hay que cuidar que el API apunte a la copia | **Todos los de Solutions**: 701 clientes reales, costos de compra y precios |
| **B. Base nueva con `db-init` + importadores** | Crear `Teikem_video`, `db-init`, `import-legacy import.solutions.json` con los CSV de `F:/Download/TeikemMigracion`, saldo inicial (`import.solutions-saldo-inicial.json`) y scripts 01 a 08 y 13 | **30 a 60 min**, con pasos manuales (el saldo exige el `ItemList.CSV` de hoy, `docs/migracion/saldo-inicial-solutions.md`) | Poco | Medio: depende de CSV fuera del repositorio; se llega al mismo contenido que A | Reales si se usan los CSV de Luis; **ninguno** con la muestra sintética `docs/migracion/sample/` (pero entonces no es Solutions) |
| **C. Instantánea de base de datos** (solo vuelve al punto de partida) | `CREATE DATABASE Teikem_video_base ... AS SNAPSHOT OF Teikem_video` | **1 a 3 s** crearla; **menos de 10 s** revertir | Archivo disperso en C:, crece solo con lo que cambia (MB) | Bajo, con tres cautelas: (1) nada conectado al revertir; (2) **no revierte** los archivos de `App_Data/files` (lo subido en una toma queda huérfano, sin daño); (3) la base de origen no se puede borrar ni restaurar mientras exista la instantánea | Los mismos que A (complementa a A) |

**Recomendación: A + C.** Copiar `Teikem` a `Teikem_video` con `COPY_ONLY`, preparar ahí lo de la sección 2, **parar el API**, crear la instantánea `Teikem_video_base` y volver a ella entre tomas. Guardar además un `.bak` de la copia **ya preparada** (sirve si se pierde la instantánea). B se descarta: tarda más, depende de archivos externos y no da una base mejor.

Cuidado con los archivos subidos: la copia usa las mismas llaves de `StoredFile`, y el API de grabación leería `src/Teikem.Api/App_Data/files`, la carpeta del desarrollo de Luis. Para no ensuciarla, copiar esa carpeta (238 MB) a otra ruta y apuntar `Files__Root` a ella. Lo que se suba en el video (firma, fotos) cae ahí y se puede restaurar.

---

## 4. Cómo se vuelve al punto de partida entre tomas

1. **Una sola vez**, con el API apagado, crear la instantánea (el `NAME` lógico del archivo de datos de la copia se ve con `RESTORE FILELISTONLY` y puede no ser `Teikem`):
   `sqlcmd -S localhost,1433 -E -C -Q "CREATE DATABASE Teikem_video_base ON (NAME = Teikem, FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\Teikem_video_base.ss') AS SNAPSHOT OF Teikem_video"`
2. **Entre tomas**, apagar el API y la ventana de Playwright y ejecutar:
   `sqlcmd -S localhost,1433 -E -C -Q "ALTER DATABASE Teikem_video SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE Teikem_video FROM DATABASE_SNAPSHOT = 'Teikem_video_base'; ALTER DATABASE Teikem_video SET MULTI_USER;"`
3. Si la toma subió archivos: `robocopy <carpeta limpia> <Files__Root> /MIR`.
4. Volver a levantar el API (así se vacían las cachés; los permisos se guardan 5 minutos en memoria según `scripts/demo/solutions-modo-contador.sql`).

Tiempo total esperado: **menos de 30 s** más el arranque del API. Sin instantánea, el equivalente es `RESTORE DATABASE Teikem_video FROM DISK = '<.bak preparado>' WITH REPLACE` (30 a 90 s). Si SQL Server se queja de que hay más de una instantánea de esa base, se borran las otras.

---

## 5. Usuarios de demostración propuestos

Se crean en la **copia**, con **nombres ficticios** (no los de las personas reales) y correos de un dominio de pruebas (por ejemplo `@example.com`). La contraseña la anota Luis en su guion; no va en este informe.

| Nombre visible | Rol | Escenas | Notas |
|---|---|---|---|
| Admin Demo | TenantAdmin | 1 productos, 2 importar compras, 3 recibo, 5 almacén, 6 QuickBooks | El que más se usa. TenantAdmin tiene 159 permisos; el manual dice que trae `sales.quickbooks.*` (`49-quickbooks.md:13`) |
| Vendedor Demo | Ventas (tras el script 03) | 4 asistente de ventas | `sales.create`, `sales.view`; verificar qué ve del costo (tramo de ventas) |
| Almacén Demo | WarehouseOperator | 5 recolección y empaque | `warehouse.pick`, `warehouse.issue` |
| Oficina Demo (papel de Leandra) | Oficina de ventas (script 03) | 6 QuickBooks | `sales.quickbooks.view/export/mark` |

Cómo se crean sin correo:
- Con el API de grabación sin Brevo: **Sistema → Roles y usuarios → Usuarios → «Nuevo usuario»**, escribiendo una contraseña (`scripts/solutions-pedido-vendedores/LEEME.md`, «Usuarios de los 7 vendedores»: esa alta no manda correo; sin contraseña muestra una temporal una sola vez).
- O por `POST /api/v1/users` con `{ email, fullName, password, roles }` (`SecurityContracts.cs:12`), con el helper `apiHeaders` de `web-app/e2e/staffHelpers.ts:35`.
- Con `Auth__Onboarding__Enabled=false` y `MfaRequired = 0` el usuario debería entrar con correo y contraseña, sin pantalla de bienvenida ni MFA. **No se probó de punta a punta**: es lo primero que hay que ensayar en la copia.
- **No** usar «¿Olvidó su contraseña?» ni invitaciones: mandan correo.

---

## 6. Variables y banderas del API de grabación

Base: lo que ya hace `scripts/api-para-pruebas.ps1` (líneas 29-34): `ConnectionStrings__Teikem` (tomada de `appsettings.Development.local.json` con `Database=` cambiado; ese archivo **no se abrió**), `ASPNETCORE_ENVIRONMENT=Development`, `Brevo__ApiKey=''`, `Auth__Onboarding__Enabled=false`, `Portal__ReturnInviteTokenInResponse=true`, `Auth__TrustedBrowsers__ReturnLinkInResponse=true`, y `--no-launch-profile --urls http://localhost:<puerto>`.

| Variable o bandera | Valor propuesto | Por qué |
|---|---|---|
| Base | `-Base Teikem_video` | El script acepta `-Base`; por omisión usa `Teikem_e2e` (sin Solutions) |
| Puerto | 5000 (o `-Puerto 5100` con `API_URL=http://localhost:5100` en Playwright) | Los recorridos asumen 5000; el script se niega si está ocupado. CORS permite 5173, 5174 y 3000 (`appsettings.json`), así que Vite debe correr en uno de esos |
| `Brevo__ApiKey` | vacío | **Regla del dueño** (CLAUDE.md). Es variable de usuario de Windows: arrancar siempre desde el script, nunca desde Visual Studio ni `preview_start` «api» |
| `Files__Root` | carpeta copia de `App_Data/files` | Para no ensuciar los archivos de desarrollo (propuesta; no está en el script) |
| Push (Firebase) | vaciar `Push__Fcm__ServiceAccountJsonPath` | `appsettings.json` (con cambios sin confirmar) apunta a un archivo de cuenta de servicio; en grabación no debe salir ninguna notificación (propuesta; confirmar el nombre de la opción) |
| `GoogleMaps__Enabled` | `false` (ya lo es en `appsettings.json`) | Sin geocodificación |
| Trabajos en segundo plano | Siguen activos: `ScheduledJobWorker` (hay **40** trabajos programados en la base), `NotificationProcessor`, `InventoryReconciliationWorker`, geocodificadores | Sin llave de correo nada sale. La reconciliación de inventario (debounce 1 500 ms) puede mover cifras en pantalla unos segundos después de recibir o despachar. Apagar trabajos en la copia con un `UPDATE` requiere aprobación de Luis; no se verificó el nombre de la columna |
| Semilla demo | `Seed:Demo:Enabled=true` (compañía Advance Depot) | No estorba: la compañía ya existe |
| `E2E_ALLOW_REAL_EMAIL` | **no poner** | `web-app/e2e/globalSetup.ts` se niega a correr si el API tiene correo configurado; con Brevo vacío pasa |
| Playwright | `TEIKEM_ADMIN_EMAIL` y `TEIKEM_ADMIN_PASSWORD` del usuario demo | Por omisión son los del administrador de la semilla de Depot (`staffHelpers.ts:11`); `login` y `apiHeaders` eligen la compañía por defecto o la primera, así que el usuario demo debe tener **solo** Solutions como compañía |

---

## 7. Datos sensibles que saldrían en pantalla

| Dato | Dónde sale | Opciones |
|---|---|---|
| **Nombres de 701 clientes reales** | Asistente de ventas (buscador de cliente), lista de Clientes, pestaña QuickBooks, órdenes | (a) Crear en la copia 3 a 5 clientes ficticios y usar solo esos; (b) renombrar clientes en la copia con un enmascarado (requiere aprobar el `UPDATE` en la copia); (c) no mostrar listas completas: escribir el cliente demo en el buscador |
| **Costos de compra** (40 productos) | Producto, valoración de inventario, importar compras | Grabar con un rol sin `inventory.value` o redondear los costos en la copia |
| **Precios de venta** reales (lista de QuickBooks) | Asistente, lista de precios, factura | Son de lista para vendedores; confirmar con Luis |
| **Proveedores y montos reales** (archivo de compras) | Importar, órdenes de compra | CSV recortado a 1 o 2 documentos; si hace falta, renombrar el proveedor en la copia y en el archivo |
| **Correos y teléfonos** | Ficha del cliente, usuarios | Usuarios demo en `@example.com`; no abrir fichas de clientes reales |
| **Nombres de empleados reales** | Los 2 usuarios actuales | Entrar con usuarios demo; no grabar Roles y usuarios antes de renombrar |
| **Datos de la compañía** (identificación fiscal, dirección) | Ajustes | No entrar a esa pantalla |

---

## 8. Dudas y decisiones para Luis

1. ¿Autoriza la base **`Teikem_video`** como copia de `Teikem` (con `COPY_ONLY`) más la instantánea? Recomiendo que sí.
2. ¿Pueden verse clientes reales en el video? Recomiendo **sustituirlos** por 3 a 5 ficticios en la copia.
3. ¿Se muestran precios de venta y costos de compra reales? Recomiendo precios sí (son de lista) y **costos no**.
4. ¿Qué kit y oferta se usan? Hay «OFERTA DEL DIA» con `KIT-001` (nombre de prueba, 8,50) o se corre el script 02 para tener OFERTA1 (41,94) con nombre presentable y foto. Recomiendo el 02 en la copia y desactivar la oferta de prueba.
5. ¿Se pueden correr en la **copia** los scripts 03 (roles), 08 y 10 (QuickBooks)? No tocan producción. El 09 (con correos) no se correría.
6. Proveedores: ¿`import-legacy --update` en la copia (proveedores reales de QuickBooks) o 2 a mano con nombres inventados y el archivo de compras editado para que coincida?
7. Escena 6: ¿se muestra en el video la corrida de facturación (genera, aprueba, pide la contraseña otra vez) o se prepara antes y solo se enseña la pestaña QuickBooks? Sin BILLING encendido y sin factura aprobada no hay nada que exportar.
8. Recibo: la zona STG no tiene posiciones y el almacén no tiene posición de recibo por omisión. ¿Se crea una posición de recibo en la copia o se recibe directo a PISO? Hay que ver el modo de recibo 311 en la escena 3.
9. No se confirmó que un usuario nuevo con contraseña y sin MFA entre sin pantallas intermedias; se ensaya en la copia antes de grabar.
10. No se midieron tiempos de respaldo, restauración ni instantánea; son estimaciones que el primer ensayo confirma.
11. No se verificó en disco que las 34 fotos existan en `App_Data/files`; solo se comprobó que las llaves están en `StoredFile` (65 imágenes).

---

## 9. Fuentes

- Base `Teikem`, solo lectura: `Tenant`, `TenantModule`, `Role`, `UserRole`, `UserTenant`, `AspNetUsers`, `Warehouse`, `WarehouseZone`, `WarehouseBin`, `Product`, `ProductKitComponent`, `StockBalance`, `Client`, `PriceList`, `Offer`, `OfferItem`, `SalesOrder`, `PurchaseOrder`, `Supplier`, `Invoice`, `TaxRate`, `TenantTaxSetting`, `StoredFile`, `ScheduledJob`, `Integration`; `SERVERPROPERTY`, `sys.database_files`, `sys.master_files`, `msdb.dbo.backupset`. Lista de bases de la instancia: `Teikem_e2e` (Logistics y Depot), `Teikem_verif213` (Depot), ninguna con Solutions salvo `Teikem`.
- `Diseño/logistica-db-update.sql:7839-7862` (lotes 238 y 240).
- `scripts/solutions-pedido-vendedores/LEEME.md`, `13-fotos-productos.ps1`, `13-fotos-productos.csv`.
- `scripts/demo/solutions-modo-contador.sql`, `solutions-nueva-posicion.sql` (busca `ALM-SOL`), `jose-demo-datos.sql` (es de Advance Depot, no sirve para Solutions); `docs/demo/guia-demo-jose.md`.
- `docs/migracion/README.md`, `saldo-inicial-solutions.md`, `import.solutions.json`.
- `scripts/api-para-pruebas.ps1`, `web-app/e2e/globalSetup.ts`, `web-app/e2e/staffHelpers.ts:10-65,134`.
- `src/Teikem.Api/appsettings.json`; `src/Teikem.Infrastructure/Services/UserAdminService.cs:69-125`; `src/Teikem.Infrastructure/Contracts/SecurityContracts.cs:12`; `src/Teikem.Infrastructure/Services/QuickBooksService.cs:30,103`.
- `docs/manual/49-quickbooks.md:1-40`, `docs/manual/19-facturacion.md:1-17`, `docs/lote239-decisiones.md`, `web-app/src/kernel/i18n/es.json:8747-8774,10453-10526`.
- Reglas de `CLAUDE.md` del proyecto (Brevo, bases, ramas, producción congelada).
- Archivos externos solo contados: `F:/Download/Solutions Purchases.CSV` (96 filas: 58 Bill, 24 Item Receipt, 1 Credit, 13 vacías) y `F:/Download/TeikemMigracion/Solutions Vendors.csv` (40 líneas).
