# Instalador de actualización con base de datos: `db-update` (2026-10-06)

Pedido del dueño: "prepara el instalador para actualizar el api y el web en staging". El instalador de Windows (`deploy/windows/instalar.ps1`) ya
reemplazaba el API y la web, pero **no tocaba la base**, y el script de estructura (`Diseño/logistica-db-estructura.sql`) **no se puede volver a correr
sobre una base ya creada** (sus `CREATE TABLE` no están protegidos: «There is already an object named 'Tenant'»; se comprobó). Sin esto, cada despliegue
a staging obligaba a recrear la base o a escribir `ALTER` a mano.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Verbo `db-update [--dry-run]` | Lleva una base **existente** a la versión actual sin borrar nada: 1) esquema, 2) seed idempotente, 3) seeders de código (permisos). Base vacía = `db-init`. Salida: 0 bien, 1 error, 2 quedó algo por revisar a mano | `Program.cs`, `DatabaseInitializer.UpdateAsync` |
| Esquema por comparación | Aplica el script de estructura a una **base temporal vacía** del mismo servidor (`<base>_shadow_<fecha>`, se borra siempre), lee el catálogo de las dos y genera **solo lo que falta**, en una transacción | `SchemaSync`, `SchemaReader` (`SchemaModel.cs`), `SchemaDiffer` |
| Qué compara | Tablas, columnas (con default, identidad, calculadas), PK/únicas, índices (con filtro e `INCLUDE`), checks, FKs (con cascadas) y vistas. Los objetos sin nombre propio (FK/check/default que nombra SQL Server) se comparan por **firma**, no por nombre | `SchemaDiffer` |
| Qué hace sola | Crear tablas, agregar columnas, crear índices/FKs/checks, **recrear** un check o índice cuyo contenido cambió (p. ej. `CK_NumberSequence_Kind` al sumar tipos), ensanchar `VARCHAR/NVARCHAR/VARBINARY`, permitir nulos, retirar una columna obsoleta `NOT NULL` sin default **solo si su tabla está vacía** (con sus FK/índices) | `SchemaDiffer` |
| Qué NO hace | Borrar tablas o columnas (lo que sobra se informa), otros cambios de tipo, volver `NOT NULL` una columna con nulos, cambiar cascadas, columnas calculadas distintas: salen como **«REVISAR A MANO»** (código 2; el resto sí se aplica) | `SchemaDiffer` |
| Registro | Al terminar anota el hash del script en `dbo.__SchemaVersion`, así que un `db-init` posterior no intenta re-aplicarlo | `SqlScriptRunner.MarkAppliedAsync` |
| Instalador | Antes de copiar archivos: **simula**, muestra el plan, pide confirmar el **respaldo**, aplica; si la base falla no se copia nada y el sitio sigue con la versión anterior. Opciones `-SinBase`, `-RespaldoHecho` | `instalar.ps1` |
| Paquete | `crear-paquete-windows.ps1` agrega `db\logistica-db-estructura.sql` y `db\logistica-db-seed.sql` al zip (y avisa de cambios sin commit también en `Diseño`) | `crear-paquete-windows.ps1` |

Sin cambio de esquema ni de contrato del API.

## Cómo se probó

- `dotnet test --filter SchemaDifferTests`: **22 pruebas** del comparador (modelos armados a mano, sin SQL Server): tabla nueva (orden de fases), columna con default,
  check cambiado, FK/check por firma, ensanche automático vs aviso, nulos, columna obsoleta con tabla vacía/con datos, sobrantes, índice y vista distintos, formato de tipos.
- Contra SQL Server local, con bases temporales (ya borradas):
  1. Base creada con los scripts de **hace una semana** (commit `0ec9abc`) → `db-update`: detectó los 111 cambios reales (tablas de Rentas, `TenantBrandLogo`, columnas de
     región/formatos, de conteo por producto y de posición provisional, el check de `NumberSequence`, las FKs) y retiró la columna obsoleta `RentalCharge.RentalContractId`
     (tabla vacía). Segunda corrida: **0 cambios**, seed omitido (mismo hash).
  2. Base casi vacía (solo `dbo.Tenant(TenantId)`) → `db-update` generó **947 cambios** (las 150 tablas con columnas calculadas, geografía, rowversion, índices filtrados, FKs
     en cascada) y la segunda corrida dio **0 diferencias**: el generador reproduce el script.
  3. El paso de base de `instalar.ps1` (extraído a un arnés) contra la base vieja: simulación, aplicación y salida limpia, sobre el paquete real descomprimido
     (`db\` presente, sin `appsettings.*.json`).
- **No probado:** la corrida completa de `instalar.ps1` en un Windows Server con IIS (no hay uno aquí); sí su sintaxis y el bloque de base de datos.

## Decisiones a revisar

1. **Permisos del usuario SQL**: la comparación crea y borra una base temporal en el servidor; el usuario de la cadena de conexión necesita `CREATE DATABASE` además de DDL.
   Si no los tiene, `db-update` falla sin tocar nada (usar `-SinBase` o un usuario con más permisos).
2. **Por qué comparar en vez de «migraciones» o scripts con guardas:** CLAUDE.md pide un solo set de scripts y nada de migraciones; envolver 150 `CREATE TABLE` en guardas habría
   cambiado el script de todo el proyecto. La comparación deja el script como está y sirve para cualquier cambio futuro sin tocar nada más.
3. **Límites conocidos**: un cambio de tipo que no sea ensanchar, o pasar a `NOT NULL`, queda en «REVISAR A MANO»; una restricción única o índice **renombrados** se detectan como
   nuevos (se crean sin quitar el viejo); datos que migrar (poblar una columna nueva con valores calculados) siguen requiriendo un script propio.
4. **Respaldo**: el instalador lo exige como confirmación, no lo hace por sí mismo (la ruta de respaldo vive en el servidor de SQL, que puede ser otro equipo).
5. Tablas sobrantes de la capa anterior de Rentas (`RentalAsset`, `RentalContract`, `RentalAssetMaintenance`, `RentalBillingRule`) quedan en la base (vacías, sin uso); se pueden
   borrar a mano cuando se confirme que no tienen datos.
