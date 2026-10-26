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

- **Atajo (2026-10-26): `scripts\versiones-descargas.ps1`** arma en `F:\Download\TeikemDescargas` la carpeta lista para subir (esta página, el logo, el icono, los APK de producción y de staging copiados con sus nombres y el `versions.json` con la versión de **todas** las apps, incluidas las de test) a partir de los APK más recientes de `F:\Download\TeikemApp`. La página tiene tarjetas de **producción** (Última versión = almacén, **Teikem Portal** y **Advance App**), **staging** (almacén) y **test** (Almacén TEST, **Teikem Portal TEST** y **Advance App TEST**). Una app sin APK conserva su versión anterior en `versions.json`; si nunca tuvo, la tarjeta dice «Todavía no se publicó» y apaga el botón. Los claves: `ultima`, `staging`, `portal`, `movil`, `test`, `clientes-test`, `movil-test`.
- Cada versión nueva a mano: copie el APK a `apk/teikem-almacen.apk` (el mismo nombre) y cambie el número en `versions.json`. Los aparatos que ya la tienen se actualizan **encima**, sin perder su configuración (misma llave de firma y código de versión mayor: ver `docs/mobile/actualizar-apk.md`).
- Staging (2026-10-12): la tarjeta «Teikem Almacén STAGING» (borde rojo punteado) descarga `apk/teikem-staging.apk`; cada versión nueva: copie el APK con ese nombre y cambie `staging` en `versions.json`. Es otra app (paquete `com.teikem.almacen.staging`): se instala junto a la de producción y se actualiza sola encima, sin tocarla.
- El servidor debe servir `.apk` como `application/vnd.android.package-archive`.
- **Test (actualizado 2026-10-26):** la sección «Ambiente de TEST» tiene las tarjetas **Teikem Almacén TEST**, **Teikem Portal TEST** y **Advance App TEST** (la app móvil del personal). Sus botones bajan de **esta misma carpeta** (`apk/teikem-almacen-test.apk`, `apk/teikem-clientes-test.apk`, `apk/teikem-movil-test.apk`), así que esos APK se suben aquí igual que los demás, y las claves `test`, `clientes-test` y `movil-test` de `versions.json` dicen la versión que muestra cada tarjeta (sin la clave, la tarjeta dice que aún no se publicó y **apaga el botón**: es lo que pasa si se sube el APK pero no se actualiza `versions.json`). `scripts/versiones-descargas.ps1` copia los APK y escribe el `versions.json` de todo. El servidor de test también publica los suyos en `https://test.advancelogisticspr.com/descargas/` (paquete de test).

## Página de descargas del ambiente de TEST (2026-10-19)
La página hermana `deploy/descargas-test/index.html` (cinta roja TEST, tarjetas de **Teikem Almacén TEST** y **Teikem Clientes TEST**) no se sube a mano: `deploy\windows\crear-paquete-windows.ps1 -Test`
la mete en el paquete con los APK más recientes de `F:\Download\TeikemApp` (`TeikemTest-*.apk` → `apk/teikem-almacen-test.apk`, `TeikemClientesTest-*.apk` → `apk/teikem-clientes-test.apk`) y escribe
su `versions.json` (`{ "almacen": "1.0.0 (c7)", "clientes": "1.0.0 (c2)" }`; sin una clave la tarjeta dice que aún no se publicó). `instalar-test.ps1` la deja en
`https://test.advancelogisticspr.com/descargas/` (el API sirve `.apk` con su tipo MIME).
