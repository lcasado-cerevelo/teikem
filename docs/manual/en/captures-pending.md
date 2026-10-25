# Captures still in Spanish (0 of 616 referenced; 616 are in English)

These images are an exact copy of the Spanish capture because no Playwright walkthrough regenerated them in English: captures of the
warehouse app and the mobile apps (taken on a device), PDFs, and screens whose walkthrough already fails with the current interface
(outdated walkthroughs, tracked in a separate task). Regenerate them with the recipe in [translation-notes.md](translation-notes.md).


## Spanish text that remains inside the English captures (it only exists in Spanish in the software)

Every English capture was taken with the interface in English. A visual review found a few texts that the **server or the app** only has in Spanish; they cannot
be changed from the walkthroughs and need a software change (an English message next to the Spanish one):

- Route and dispatch notices: "El vehículo no tiene documentos registrados.", "N paradas tienen ubicación aproximada o sin coordenadas.", "La ruta no tiene vehículo asignado.", "El estatus actual no permite la acción 'ASSIGN_TRIP'." (fe2a-*, fe2b-*).
- Scan station results: "No se encontró la pieza.", "Asignado a la ruta … (brincó N estatus)", "No hay ruta abierta para la zona …" (fe1-*).
- Piece statuses of claims in the client portal ("Por verificar", "Perdido", "Reclamada") and the scan notice "La pieza … apareció…" (l111-*).
- Suggested bin reasons "Consolidar con el mismo producto", "Reserva vacía" (f6-tarea-completar, l16-recibo-directo-*); "Envío: ORD-… · Factura: FAC-…" on pick batch cards (f6-recolecciones).
- Job results and system notices saved in Spanish ("Ninguna factura nueva por vencer.", "Prueba completada.", bell notices): l50-*, l52-campanita; COD run details (l85-corrida); freight line "Flete STANDARD/BOX" (l119-factura); "Envío ORD-…" and "Ajustado:" in driver settlement (l96-ficha, l96-exportada).
- Invoice-check results "Factura procesada" and "No entregada" and their detail texts (l126-corrida): the English keys are missing in `web-app/src/kernel/i18n/en.json`.
- Trusted-computer names such as "Chrome en Windows" (l100-lista, changed to "on" in the test database only), legal-text placeholders "[RAZÓN SOCIAL DEL OPERADOR]" (l102-legal, translated in the response during the capture only), "Invalid code." / PIN and process errors from the server (f1-mi-cuenta-mfa-activar, f8a-pin-trivial-rechazado, f18-proceso-avanzar-error).
- The label key `warehouse.inventory.filters.zone` appears raw in the movement ledger filters (f6-inventario-saldos, f19-d-kardex, f7a-inventario-*): the key is missing in the app dictionaries.
- Kemi's answers are in Spanish because the help index is built from the Spanish manual (l218-*).
