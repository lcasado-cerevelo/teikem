# Lote 206 — Entregar / no entregar desde la app del chofer (servidor)

## Qué se construyó
- `GET /driver-app/shipments/{id}`, `POST …/deliver`, `POST …/fail`, `GET /driver-app/failure-reasons` en `DriverAppController` / `DriverAppService`.
- Entregar reutiliza `DeliveryRegistrationService.RegisterAsync` (nuevo parámetro opcional `DeliveryRegisterOptions`: origen DRIVER_APP, firma RECIPIENT, texto de bitácora y GPS del teléfono, que manda sobre la dirección). La firma y fotos (base64) se guardan en el almacén de archivos del comprobante; el intento (`DeliveryAttempt`) queda con origen APP, el chofer y la hora real.
- No entregar usa `StatusService.TransitionAsync` a FAILED; el intento fallido lleva el motivo de «Razón de fallo». La foto opcional se guarda como DAMAGE_PHOTO del envío.
- Idempotencia para la cola sin señal; ajenos = 404; sin aceptar = 422.

## Cómo se probó
4 pruebas nuevas en `TripStatusEffectTests.DriverApp.cs` (39 de la clase en verde): sin aceptar, entregar + reintento, validaciones y ajeno, fallar con motivo. OpenAPI y tipos regenerados.

## Decisiones a revisar
- **COD**: no se registra aún el cobro contra entrega desde la app (el módulo `Cod` es aparte). Falta decidir si el chofer captura el monto cobrado y el tipo (efectivo/cheque…).
- **Fotos/firma obligatorias**: Colavora lo decidía por cliente (`PhotoOnDelivery`, `SignatureOnDelivery`). En Teikem hoy son opcionales; si se quieren obligatorias por cliente hace falta un campo en el cliente.
- **Teléfono de contacto**: se toma del `ContactPoint` principal de la orden; si la oficina no lo captura, la app no lo muestra.
- Piezas individuales y comentarios por envío (Colavora) no se llevan: Teikem solo tiene el total de piezas.
