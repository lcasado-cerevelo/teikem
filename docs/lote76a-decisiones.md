# Lote 76a — Kemi con vida y arrastrable (2026-10-16)

Pedido de Luis, antes del Lote 76: que Kemi parezca estar ahí, mirando e interactuando; parpadee de verdad, se quede pendiente con el mouse encima, brinque con una notificación, se pueda poner donde él quiera, y todo lo más liviano posible.

## Qué se construyó
- **Gestos**: reposo, parpadeo, mirar a la izquierda o la derecha, guiño, feliz, sorprendida, lengua, preocupada y «pendiente» (con el puntero encima). `kemiLife.ts` (reglas puras: esperas al azar, qué gesto toca, posición) y `useKemiLife.ts` (temporizadores).
- **Dibujo**: `KemiMascot.tsx` es un solo SVG con las piezas de todos los gestos (ojos con pupilas, cinco bocas); `notifications.css` enseña lo que toca por `data-mood` y `data-blink`, solo con `transform` y `opacity`.
- **Aviso nuevo**: `excited` (ojos y boca grandes mientras el globo está a la vista) y el brinco de tres veces.
- **Arrastrable**: se agarra y se suelta, se guarda por usuario (`teikem.kemi.pos.<usuario>`), se mantiene dentro de la ventana y abre el globo y el panel hacia donde hay lugar. Opción «Llevar a Kemi a su esquina».
- **Arreglo de paso**: la regla global `svg{width:16px;height:16px}` de `styles/base.css` dejaba a Kemi diminuta; ahora el dibujo lleva su tamaño en línea.
- Manual de pantallas (L52 §4) actualizado.

## Rendimiento
Sin imágenes (un SVG), sin ciclo de animación y sin estado de React que cambie al animar: los temporizadores solo cambian atributos del dibujo. No corren con la pestaña oculta, con el puntero encima, con «reducir movimiento», dormida o con el panel abierto. Las pupilas siguen al puntero solo mientras está encima y a un cuadro por pantalla como máximo.

## Cómo se probó
- vitest: reglas puras (esperas dentro del rango, gesto que no se repite, pupilas con tope, posición, anclaje, guardado y datos dañados) y el temporizador con relojes falsos (parpadea y reabre los ojos, el puntero encima, «reducir movimiento», limpieza al desmontar). `npm run check` completo: 242 archivos, 2931 pruebas.
- En el navegador (servidor local): los diez gestos se dibujan bien, parpadea a intervalos distintos, se arrastró a otro lado, se guardó y volvió al recargar, el panel se abre hacia abajo sin correr a Kemi, y «Llevar a Kemi a su esquina» la devuelve.

## Decisiones a revisar con Luis
1. **No había 10 imágenes en el proyecto**: Kemi era un dibujo vectorial. Los gestos se hicieron con ese mismo dibujo (más liviano que imágenes). Si Luis tiene sus 10 imágenes aparte, se pueden usar en lugar de estos gestos.
2. Los gestos de la lista son los de arriba; se pueden agregar o quitar sin tocar el motor (son una lista en `kemiLife.ts`).
3. Las pupilas siguen solo el puntero cuando está encima de Kemi (seguirlo por toda la pantalla costaría más y distraería).
