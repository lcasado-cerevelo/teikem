# Lote 162 — Teléfonos: solo dígitos y prefijo internacional por número

Fecha: 2026-10-20. Pedido de Luis (dictado): el teléfono se guarda solo con números; cada número lleva su prefijo internacional en un campo aparte («aunque todos son de Estados Unidos, para que sea full»); el formato de despliegue se define por compañía; revisar la aplicación completa por todo dato de teléfono.

## Lo que se construyó

- **Reglas puras** (`Teikem.Domain.Contacts.PhoneRules`): separa un teléfono escrito a mano en prefijo y número nacional (solo dígitos); acepta formato (`(787) 555-1234`, `787.555.1234`), «+» al inicio, un 11 dígitos con 1 inicial (prefijo 1) y el prefijo indicado aparte. Arma E.164 (`ToE164`).
- **`ContactPoint.CountryCallingCode`** (`VARCHAR(4) NULL`, solo dígitos y sin «+»; Luis aprobó el nombre). En los tipos PHONE, MOBILE, FAX y WHATSAPP `Value` queda con solo los dígitos nacionales y el prefijo se llena siempre (el indicado, el del «+», o el de la compañía). `ContactPointDto` y `ContactPointUpsertRequest` ganan `countryCallingCode` (último parámetro, opcional).
- **`Supplier.PhoneCountryCallingCode`** (`VARCHAR(4) NULL`) y el teléfono del proveedor, con solo dígitos; `SupplierRequest`, `SupplierPatchRequest` y `SupplierDto` ganan `phoneCountryCallingCode` (último, opcional).
- **SMS en E.164**: `ConsigneeNoticeService.ContactsAsync` devuelve el teléfono del consignatario como `+prefijo+dígitos`; así el SMS y la baja del aviso trabajan con el mismo formato (los datos viejos con formato también salen bien).
- **Migraciones de datos**: `Importaciones` (órdenes, consignatarios, plantillas, QuickBooks/Legacy) validan con `ContactPointService.ValidateValue`, que ahora quita el formato y conserva el «+»; el prefijo definitivo lo pone `AddAsync`. La importación de Colavora separa prefijo y número al crear el contacto.
- **Sección SQL** (`Diseño/logistica-db-update.sql`, «Lote 162»): agrega las dos columnas y limpia lo ya guardado (idempotente). Probada en una base desechable con casos de formato, «+1», «+593» en una compañía de EE. UU. y de Ecuador, texto con extensión y correos; segunda corrida sin cambios.
- **Web** (`web-app`): el campo **Prefijo** (`CallingCodeInput`, junto a `PhoneInput`, en `PhoneRow`) está en los contactos (`ContactPointsPanel`: cliente, órdenes, choferes…), las personas de contacto del cliente, el consignatario y el proveedor; arranca con el prefijo de la compañía. Con el prefijo de la compañía rige su máscara; con otro, de 7 dígitos hasta 15 en total, y se muestra «+593 991234567» (`formatPhoneFor`, `f.phoneFor`). `useFormat` suma `phoneFor`, `isValidPhoneFor`, `normalizePhoneFor`, `callingCodeInput`, `usesCompanyMaskFor`.
- **Formato por compañía**: ya existía en **Ajustes → Región y formatos** (`phoneCountryCode` y `phoneMask`, Lote F9); no hubo que crear campos nuevos de compañía. El prefijo por omisión de cada número sale de `phoneCountryCode`.

## Cómo se probó

- C#: `PhoneRulesTests` (nuevo) y las pruebas de proveedores, importaciones y avisos al consignatario actualizadas; suite completa 4966 pasan, 0 fallan, 54 omitidas (las de siempre). Se compiló a una carpeta aparte porque la API estaba corriendo y bloqueaba los DLL.
- SQL: probado en una base desechable (`PhoneScratch`, ya borrada), dos corridas.
- Web: `npm run check` (tipos generados, tsc, oxlint, vitest, build) sin errores.
- No se probó contra la base local `Teikem` ni en el navegador.

## Decisiones a revisar

1. **El prefijo se guarda siempre** (no se deja vacío «= el de la compañía»), para que un número quede completo aunque la compañía cambie su prefijo por omisión. En los datos antiguos que no se pudieron limpiar queda vacío y se interpreta con el de la compañía.
2. **Datos antiguos que se dejaron sin tocar**: números con texto (extensión, letras) y los que traen «+» con un país distinto al de la compañía. Hay que revisarlos a mano (FAQ).
3. **Mínimo 7 dígitos, máximo 15 con el prefijo** (E.164) para cualquier país; para el prefijo de la compañía la pantalla exige los dígitos de la máscara (10 en Puerto Rico), como antes.
4. **Bajas de SMS antiguas**: `ConsigneeUnsubscribe.Contact` guardaba el teléfono con el formato viejo; las nuevas llegan en E.164. Las antiguas dejan de coincidir (los avisos por SMS se vuelven a ofrecer a quien se dio de baja por el formato anterior). Si importa, se migran con un script.

## Pendiente (revisión de la aplicación completa, 2026-10-20)

Datos de teléfono que **no** se tocaron en este lote:

| Dato | Dónde | Hoy | Propuesta |
|---|---|---|---|
| Teléfonos extra de aviso del cliente | `ClientNotificationSettings.ExtraPhones` (texto, hasta 400, varios separados) | texto libre con formato | guardar cada uno en E.164 (`+17875550100`) |
| Destinatarios de reglas de aviso, tipo PHONE | `NotificationRuleRecipient.Value` | texto libre | E.164 al guardar |
| Teléfono del usuario | `User.PhoneNumber` (`NVARCHAR(50)`) | texto libre | E.164 al guardar (lo usa el SMS de aviso y el de prueba) |
| Segundo factor por SMS | `UserMfaFactor.PhoneE164` | ya es E.164 | nada |
| Pantallas y apps | `app-almacen`, `app-cliente` | solo muestran el teléfono del consignatario | usar `+prefijo dígitos` (ya viene en E.164) |

Para esos tres campos de texto el prefijo viaja dentro del valor (E.164): es una sola columna y puede haber varios. Falta que Luis lo confirme.
