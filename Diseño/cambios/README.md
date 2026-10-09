# Cambios a la base de datos (desde la puesta en producción, 2026-10-09)

> **`Diseño/logistica-db-estructura.sql` y `Diseño/logistica-db-seed.sql` están CONGELADOS. No se editan. Nunca.**
> Un cambio de estructura **o de datos** va en un archivo NUEVO en esta carpeta. Una prueba (`FrozenSqlTests`) falla si alguien toca los congelados.

## Reglas
1. Un archivo por cambio: `NNNN-descripcion-corta.sql` (`0001-…`, `0002-…`). Se aplican en orden de nombre.
2. **Idempotente**: se puede correr dos veces sin daño. Columnas: `IF COL_LENGTH('dbo.T','C') IS NULL ALTER TABLE …`. Tablas: `IF OBJECT_ID('dbo.T','U') IS NULL CREATE TABLE …`. Datos: `MERGE` (o `IF NOT EXISTS`).
3. Se separa en lotes con `GO`. Las FKs respetan el orden de capas.
4. Nunca se edita un archivo de aquí ya publicado (el runner lo aplica una vez por hash: editarlo lo volvería a correr); se corrige con un archivo nuevo.
5. Se aplican solos en `db-init` y en `db-update` (después de estructura y seed). El instalador de Windows los copia a `db\cambios`.
6. El mapeo de EF (`Persistence/Configurations`) sí se edita con normalidad.
7. Los scripts de recrear la base (`db-reset`, `scripts/recrear-base*.ps1`) están **prohibidos fuera de desarrollo**.
