# Recrear la base local con los datos de producción

Un solo script borra la base local de Teikem, la vuelve a crear desde `Diseño/` y migra las dos compañías:

| Compañía | De dónde salen los datos |
|---|---|
| **Advance Depot** | QuickBooks (`Depot Products/Customers/Vendor.csv`) + inventario por posición del **WMS MSWM de producción** (almacén `Main`) |
| **Advance Solutions** | QuickBooks (`Solutions Items/Customers/Vendors.csv`); su inventario sale de QuickBooks |

**Siempre recrea** (decisión de Luis, 2026-09-30): no actualiza nada encima de lo que haya. Todo lo que exista en la base
local se pierde: la demo Advance Logistics y lo que se probó en ella, lo creado a mano en Depot o Solutions, los aparatos
registrados y los PIN. Del MSWM de producción **solo se lee** (la conexión va con `ApplicationIntent=ReadOnly`).

## Lo que hace falta (una sola vez)

1. **Los 6 CSV de QuickBooks** en `F:\Download\TeikemMigracion\`, con estos nombres exactos:
   `Depot Products.csv`, `Depot Customers.csv`, `Depot Vendor.csv`, `Solutions Items.csv`, `Solutions Customers.csv`,
   `Solutions Vendors.csv`. Para refrescarlos, exporte de nuevo desde QuickBooks y reemplace los archivos.
2. **La variable de entorno `ConnectionStrings__LegacyMswm`** con la conexión al MSWM de producción. Se crea con:

   ```
   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\crear-variable-mswm.ps1"
   ```

   Pide servidor (hoy `172.31.40.124\sqlexpress`), base (`MSWM`), tipo de autenticación, usuario y contraseña; prueba la
   conexión y la guarda como variable **de usuario de Windows**. La contraseña no queda en ningún archivo del repositorio.
   Después de crearla o cambiarla, abra una PowerShell nueva (las abiertas antes no la ven).

## Recrear

1. Detenga el API si está corriendo (el script se niega a seguir si hay algo escuchando en 5000/5001).
2. En una PowerShell:

   ```
   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\recrear-base.ps1"
   ```

3. Escriba `RECREAR` cuando lo pida.

El script, en orden:

| Paso | Qué hace                                                                              | Si falla                                  |
| ---- | ------------------------------------------------------------------------------------- | ----------------------------------------- |
| 1    | Conecta al MSWM de producción y muestra cuántos artículos y existencias de `Main` hay | Se detiene **sin borrar nada**            |
| 2    | Comprueba que estén los 6 CSV y muestra su tamaño y fecha                             | Se detiene sin borrar nada                |
| 3    | Comprueba que el API esté detenido                                                    | Se detiene sin borrar nada                |
| 4    | Pide confirmación (`RECREAR`)                                                         | Cancela sin borrar nada                   |
| 5    | Compila el API                                                                        | Se detiene sin borrar nada                |
| 6    | `db-reset --yes`: borra la base `Teikem` y la crea con estructura, seed y la demo     | Se detiene                                |
| 7    | `import-legacy docs/migracion/import.depot.json`                                      | Sigue con Solutions y lo informa al final |
| 8    | `import-legacy docs/migracion/import.solutions.json`                                  | Lo informa al final                       |
| 9    | Resumen: informes generados y bitácora                                                | —                                         |

Tarda unos minutos. Al final debe decir **"Listo: base recreada, Depot y Solutions migradas sin rechazos graves."**

## Qué deja

- En `F:\Download\TeikemMigracion\`:
  - `reporte-depot-AAAAMMDD-HHMM.md` y `reporte-solutions-…md` con sus CSV (resumen, rechazos, advertencias, mapeos,
    saldo inicial). Ábralos para revisar lo que no entró.
  - `recrear-AAAAMMDD-HHMM.log`: todo lo que salió en pantalla.
- El saldo inicial entra como ajuste con motivo *Saldo inicial* y la nota de `import.*.json` (`openingBalances.notes`).
  Cambie esa fecha en los dos JSON si quiere otra.

## Después de recrear

1. Arranque el API.
2. Para la app del almacén: registre de nuevo el aparato (**Sistema → Aparatos**) y asigne los PIN
   (**Sistema → Usuarios → Asignar PIN**); los anteriores se borraron con la base.

## Opciones

| Opción | Para qué |
|---|---|
| `-Carpeta "D:\otra\carpeta"` | Los CSV están en otra carpeta (el script usa copias temporales de los JSON con esa ruta). |
| `-SinConfirmar` | No pregunta `RECREAR` (para correrlo sin atenderlo). |

## Problemas frecuentes

| Mensaje | Qué hacer |
|---|---|
| *Falta la variable ConnectionStrings__LegacyMswm.* | Corra `scripts\crear-variable-mswm.ps1` y abra otra PowerShell. |
| *No se pudo conectar al MSWM: …* | Revise que el servidor de producción esté accesible (red/VPN) y el usuario y la contraseña; vuelva a crear la variable. |
| *Faltan en F:\Download\TeikemMigracion: …* | Copie ahí los CSV con los nombres exactos. |
| *El API está corriendo (puerto 5000/5001).* | Deténgalo (Ctrl+C en su consola o cierre Visual Studio) y repita. |
| *Terminó con rechazos graves* | Abra el `reporte-…md` de la compañía indicada: la sección de rechazos dice qué fila y por qué. |
| *La cadena de conexión no apunta a un servidor local* | `ConnectionStrings:Teikem` apunta a otro servidor; el script solo recrea bases locales. |

Para el detalle del importador (qué se migra, mapeos, `--dry-run`) ver [README.md](README.md).


---

# Solo Advance Depot (para el deploy) — `scripts\recrear-base-depot.ps1`

Igual que `recrear-base.ps1` pero **sin Advance Solutions y sin la demo Advance Logistics**: la base queda con **una sola compañía, Advance Depot**
(QuickBooks de Depot + el inventario por posición del MSWM de producción, almacén `Main`, hoy `172.31.40.124\sqlexpress`). El script apaga el seed de la
demo solo para su proceso (`Seed__Demo__Enabled=false`, lo mismo que hace el instalador del deploy). Del MSWM solo se lee.

```
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\recrear-base-depot.ps1"
```

| Opción | Para qué |
|---|---|
| `-Carpeta "D:\otra"` | Carpeta de los **3** CSV de Depot (`Depot Products.csv`, `Depot Customers.csv`, `Depot Vendor.csv`); los de Solutions no hacen falta. |
| `-Conexion "Server=IP,1433;Database=Teikem;User Id=…;Password=…;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True"` | Base **destino** (la del servidor de producción). Sin ella el API usa **su configuración de siempre** (secretos de usuario `teikem-api`, variable `ConnectionStrings__Teikem` o `appsettings`); el script solo la lee para mostrarle a dónde va a borrar y **prueba el acceso antes de borrar**. |
| `-PermitirRemoto` | Obligatorio si la base destino no es de este equipo (el API se niega a borrar una base remota sin ello). |
| `-SinConfirmar` | No pregunta. Sin este switch, en una base **remota** hay que escribir el nombre de la base; en una local, `RECREAR`. |

Qué hace de más que el de siempre:
- Muestra **de dónde lee** (servidor y base del MSWM) y **qué va a borrar** (servidor, base y si es LOCAL o REMOTO) **antes** de tocar nada.
- Se niega con cualquier base que empiece con `MSWM`.
- Pide la **contraseña inicial del administrador de Depot** (`teikem+admin@cerevelo.com`); no se escribe en ningún archivo (viaja por la variable
  `TEIKEM_IMPORT_ADMIN_PASSWORD` solo durante la migración). Si se deja vacía se genera una temporal que no se muestra y hay que entrar por
  «¿Olvidó su contraseña?» (necesita el correo configurado). Igual el usuario pasa por el **primer ingreso** (correo, contraseña propia y MFA).
- Al terminar **verifica la base**: debe haber **1 compañía**, y muestra productos, posiciones, saldos con existencia, clientes y proveedores.

Qué NO trae esta base: la demo Advance Logistics, el almacén ALM-01, los usuarios de prueba (`teikem+dispatch@…`) ni el usuario de plataforma de la demo
(`teikem+support@cerevelo.com`). **No existe otro medio de crear un administrador de plataforma**; si lo necesita en producción, hay que decidirlo aparte.

Orden recomendado en el deploy: 1) instalar con `deploy\windows\instalar.ps1` (o el paquete que use) para crear el sitio y la configuración;
2) con el API detenido, correr este script apuntando a la base de producción (`-Conexion … -PermitirRemoto`); 3) arrancar el API;
4) registrar el aparato y asignar los PIN.

## Administrador de plataforma (soporte)

El script marca al administrador de Depot (`teikem+admin@cerevelo.com`) también como **administrador de plataforma** (variable
`TEIKEM_IMPORT_ADMIN_PLATFORM=1` durante la migración): puede operar cualquier compañía. No se crea un usuario `support` aparte (ese solo lo
siembra la demo). Para otras migraciones, ponga la misma variable antes de `import-legacy`.
