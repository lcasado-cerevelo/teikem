# Página de descargas del APK (Teikem Almacén)

Página estática, igual en estilo a la de Advance App. Súbala a cualquier servidor web (por ejemplo `https://.../descargas/`) con esta estructura:

```
index.html
logo.png
favicon.ico
versions.json          { "ultima": "1.0.0" }  → el número que se muestra
apk/teikem-almacen.apk El APK que deja scripts\construir-apk.ps1 (renómbrelo a este nombre)
```

- Cada versión nueva: copie el APK a `apk/teikem-almacen.apk` (el mismo nombre) y cambie el número en `versions.json`. Los aparatos que ya la tienen se actualizan **encima**, sin perder su configuración (misma llave de firma y código de versión mayor: ver `docs/mobile/actualizar-apk.md`).
- Para una versión de prueba, descomente la tarjeta «Prueba» en `index.html` y ponga `apk/teikem-almacen-prueba.apk`.
- El servidor debe servir `.apk` como `application/vnd.android.package-archive`.
