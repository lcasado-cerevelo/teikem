# Avisos al consignatario y página pública con enlace — diseño (2026-10-18)

Dictado por Luis. **Todavía sin código** (lote aparte, después de piezas y escaneo por pieza). Reemplaza lo dicho antes sobre quién maneja la aceptación de avisos del consignatario.

## Decidido
1. **La página pública se queda**, porque la mayoría de los consignatarios no tendrá acceso al portal. Pasa a ser **solo de consulta por enlace**: el aviso trae un enlace con un **token propio, imposible de adivinar**, y al abrirlo se ve el resultado; la página **no recibe datos** (se quita la búsqueda manual por número de conduce).
2. **Cuándo se avisa:** si el consignatario tiene email o teléfono, aceptó recibir avisos por ese medio y el cliente tiene prendido el aviso al consignatario: el **enlace sale cuando la orden sale En ruta** y se sigue avisando de **cualquier estatus siguiente**: intento fallido, otra salida En ruta, otro intento… y **también de los finales** (Entregada, Devuelta, Cancelada, cualquiera). **No** se avisa de los movimientos dentro del almacén (Recogida, Sin asignar, Asignada) ni de los estatus brincados.
3. **Quién lo prende y marca:** **el cliente** (no la compañía): prende el aviso al consignatario y marca, por consignatario, si acepta correo y/o teléfono. Al marcarlo sale una **advertencia** (parte de la política de privacidad, «si desea continuar»); queda **en la bitácora**; y **se le notifica al consignatario**, por el correo o el teléfono marcado, que el cliente tal lo marcó para que reciba avisos.
4. **Toda comunicación al consignatario** (de cualquier tipo) trae la **opción de darse de baja**: enlace en el correo; «STOP» en el texto. Baja registrada.
5. **Idioma del aviso: el del cliente.**
6. El texto (SMS) sigue esperando a Twilio; primero correo.

## Por construir (lote aparte)
Ajuste del cliente (aviso al consignatario + marcas por consignatario, con advertencia y bitácora), aviso de alta al consignatario, token por orden para el enlace, página pública solo por enlace, eventos de aviso por estatus (En ruta, intento fallido, finales), baja por correo/texto, pruebas, manual y FAQ.
