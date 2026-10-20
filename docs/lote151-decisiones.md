# Lote 151 — Colores de la marca (pedido de Luis, 2026-10-20)

## Qué estaba mal
- El muestrario de temas pintaba `flow`, `flow` y `money` (dos naranjas y un gris): el azul marino —que es el **tono base** de los fondos— nunca se mostraba.
- La ayuda decía que el color primario era de «barras y navegación», pero el menú lateral y la barra de arriba usaban las superficies derivadas del neutro (gris en claro, azul oscuro apagado en oscuro). El naranja solo salía en el ítem activo, botones y enlaces; los títulos y las ventanas usaban el color de texto.

## Qué cambió
- **Barras y navegación en azul marino con letras blancas, en los dos modos:** menú lateral, barra de arriba y encabezado de las ventanas (`--navbar`, `--navbar-hover`, `--on-navbar`…). El azul sale del tono base del tema (`deriveBar`, se oscurece hasta 7:1 con el blanco); con el tema por omisión es el #031F49 (≈ #03204B de la página de Advance). El ítem activo lleva el naranja de la acción.
- **Títulos de pantalla** en azul marino (claro) o en el texto principal (oscuro) con una línea corta naranja debajo (`--title`).
- **Logo:** la barra lateral usa siempre la variante para fondo oscuro (`onDark`).
- **Muestrario de temas** con los tres colores: azul marino (barras), naranja (acción) y gris (acento). La muestra de «Así se ven los tres colores» pinta la barra en azul marino. Las ayudas de «Color primario» y «Tono base» dicen ahora lo que hacen.
- El tema por omisión se llama **Enterprise** (antes «Advance Logistics (por defecto)»); la clave interna `advance` no cambia. «Advance Logistics» sigue siendo el nombre de la compañía donde ya se usa.

## Cómo se probó
- vitest: tokens por omisión = tema calculado (oscuro y claro, incluidos los nuevos), logo invertido en la barra, menú, ajustes de marca; xunit de la marca (84) con los vectores compartidos sin cambios (la validación no cambia: el azul de las barras no es un acento, su contraste con el blanco queda garantizado al calcularlo). `npm run check` verde.
- No hay capturas en esta sesión (no hay API ni base): conviene que Luis revise los dos modos en pantalla.

## Decisiones a revisar
1. **El gris «clarito» de los fondos del modo claro no cambió**: sigue siendo el gris azulado derivado del tono base (#EFF3F8). Bajarle la saturación al modo claro exige regenerar los vectores compartidos con el servidor; es un paso aparte si lo quiere más neutro.
2. El gris del **acento** (#6C757D) se queda: más claro no pasa el contraste mínimo 4.5:1 como texto.
3. Con colores propios, el azul de las barras se calcula del «Tono base» (no hay un selector aparte para las barras).
