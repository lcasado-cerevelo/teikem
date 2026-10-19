# Página de descargas de las apps (producción, staging y test)

Página estática, igual en estilo a la de Advance App. Súbala a cualquier servidor web (por ejemplo `https://.../descargas/`) con esta estructura:

```
index.html
logo.png
favicon.ico
versions.json          { "ultima": "1.0.0", "staging": "1.0.0" }  → los números que se muestran
apk/teikem-almacen.apk El APK de producción que deja scripts\construir-apk.ps1 (renómbrelo a este nombre)
apk/teikem-staging.apk El APK de staging que deja scripts\construir-apk.ps1 -Staging (renómbrelo a este nombre)
```

- Cada versión nueva: copie el APK a `apk/teikem-almacen.apk` (el mismo nombre) y cambie el número en `versions.json`. Los aparatos que ya la tienen se actualizan **encima**, sin perder su configuración (misma llave de firma y código de versión mayor: ver `docs/mobile/actualizar-apk.md`).
- Staging (2026-10-12): la tarjeta «Teikem Almacén STAGING» (borde rojo punteado) descarga `apk/teikem-staging.apk`; cada versión nueva: copie el APK con ese nombre y cambie `staging` en `versions.json`. Es otra app (paquete `com.teikem.almacen.staging`): se instala junto a la de producción y se actualiza sola encima, sin tocarla.
- El servidor debe servir `.apk` como `application/vnd.android.package-archive`.
- **Test (2026-10-19):** la sección «Ambiente de TEST» tiene las tarjetas **Teikem Almacén TEST** y **Teikem Clientes TEST** (el portal del cliente, que solo existe en test). Sus botones NO bajan de esta carpeta: apuntan a `https://test.advancelogisticspr.com/descargas/apk/teikem-almacen-test.apk` y `.../teikem-clientes-test.apk`, que publica el paquete de test (`crear-paquete-windows.ps1 -Test` + `instalar-test.ps1`), así que no hay que copiar esos APK aquí. Lo único que se cambia aquí son las claves `test` y `clientes-test` de `versions.json` (el número que muestra la tarjeta; sin la clave dice que aún no se publicó y apaga el botón).

## Página de descargas del ambiente de TEST (2026-10-19)
La página hermana `deploy/descargas-test/index.html` (cinta roja TEST, tarjetas de **Teikem Almacén TEST** y **Teikem Clientes TEST**) no se sube a mano: `deploy\windows\crear-paquete-windows.ps1 -Test`
la mete en el paquete con los APK más recientes de `F:\Download\TeikemApp` (`TeikemTest-*.apk` → `apk/teikem-almacen-test.apk`, `TeikemClientesTest-*.apk` → `apk/teikem-clientes-test.apk`) y escribe
su `versions.json` (`{ "almacen": "1.0.0 (c7)", "clientes": "1.0.0 (c2)" }`; sin una clave la tarjeta dice que aún no se publicó). `instalar-test.ps1` la deja en
`https://test.advancelogisticspr.com/descargas/` (el API sirve `.apk` con su tipo MIME).
