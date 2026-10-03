# App de almacén (Zebra) — mejoras de uso detectadas al probar en el aparato — diseño acordado con el dueño

Estado: **implementado en el Lote A3 (`docs/mobile/loteA3-decisiones.md`), pendiente de la lista de comprobación en el Zebra**. Fecha: 2026-10-03. Origen: pruebas de Luis en el Zebra. Va junto con el lote de la app de "Contar por producto" (`docs/conteo-por-producto-diseno.md`), pero es independiente de él y del servidor: se puede hacer primero.

**Regla de diseño de todo este documento:** los usuarios del almacén no son técnicos; hay que mostrarles todo grande y claro, y quitarles toques innecesarios.

**Lo que NO cambia:** quién ve las cantidades del sistema al contar (sigue dependiendo del permiso `warehouse.count`, como hoy).

## 1. El último botón queda tapado por la barra de navegación del aparato
- **Causa en el código:** `app/_layout.tsx` aplica solo el margen **superior** del sistema (`paddingTop: insets.top`); el inferior no se aplica, y ninguna pantalla usa `SafeAreaView`. Con la barra de navegación desplegada, queda encima del contenido de abajo y tapa más de la mitad del último botón.
- **Cambio:** aplicar un margen inferior una sola vez en `AppContent`: `paddingBottom = max(insets.bottom, 32) + spacing.sm`. El mínimo de 32 dp es **más de la mitad del alto del botón** (los botones miden 56 dp; la mitad es 28), de modo que aunque el aparato reporte un margen de 0 el botón se ve completo, y si lo reporta bien se usa el real.
- **Opcional, no incluido por defecto:** ocultar la barra del aparato (modo inmersivo con `expo-navigation-bar`). Se evalúa solo si el margen no basta, porque en un Zebra con botones físicos o gestos oculta la forma de salir de la app.
- **Prueba:** pantallas con botón al fondo (Recibir, Acomodar, Despacho, Conteo, Consulta, Sincronización), con barra de 3 botones y con gestos.

## 2. El lector (trigger del Zebra) muestra el teclado y no avanza solo
- **Lo que hay hoy:** `ScanField` ya ejecuta "Aceptar" cuando le llega una lectura por **intent** de DataWedge (`useScanner`) o un Enter; es decir, el diseño ya es "escanear = aceptar". Lo que ve Luis (aparece el teclado y hay que tocar Continuar) indica que la lectura **no está llegando por el intent** y que se está escribiendo como teclas en el campo enfocado.
- **Hallazgo en el código:** el perfil `TeikemAlmacen` que crea el módulo `DatawedgeModule.kt` configura los plugins `BARCODE` e `INTENT`, pero **no** el plugin `KEYSTROKE`; un perfil nuevo de DataWedge trae la salida por teclas **encendida por defecto**. Además la creación del perfil se envía con `SEND_RESULT=false`, así que si DataWedge la rechaza nadie se entera.
- **Causa probable, a confirmar en el aparato:** el perfil no se está aplicando a la app (o se aplica con la salida por teclas encendida) y gana el perfil por defecto del Zebra.
- **Cambios:**
  1. Apagar explícitamente `KEYSTROKE` en el perfil (`keystroke_output_enabled = false`) y pedir el resultado de la creación (`SEND_RESULT=true`), registrándolo.
  2. En `ScanField`: `showSoftInputOnFocus={false}` para que **el teclado en pantalla no aparezca** al escanear ni al enfocar el campo, con un botón pequeño "⌨" para mostrarlo cuando se quiera escribir a mano. El campo sigue recibiendo Enter y el botón Aceptar como hoy.
  3. Un indicador "Lector: listo / sin perfil" en la pantalla de Sincronización, para ver en el aparato si el perfil quedó aplicado.
- **Alcance de "escanear = aceptar":** en **todas** las pantallas con campo de escaneo: Recibir, Acomodar, Despacho, Conteo (posición y producto) y Consulta. Escanear equivale a escribir el código y tocar Aceptar del campo. Los pasos de **cantidad** siguen siendo manuales.
- **Prueba:** no se puede comprobar el lector en este entorno; la prueba es en el Zebra con el APK que genera el CI (lista de comprobación en el documento de decisiones del lote). Las pruebas unitarias cubren el campo (sin teclado al enfocar, botón ⌨, Aceptar al escanear).

## 3. Conteo: tocar un producto de la lista para llenar el campo
- Hoy, en el conteo, después de escanear la posición aparece la lista de productos de esa posición, y tocar un producto **no hace nada**: si el producto no tiene etiqueta hay que teclear el código.
- **Cambio:** tocar un producto de la lista **llena el campo del producto** con su código (y deja el cursor listo); se confirma con Aceptar como siempre. `ScanField` recibe un valor sugerido desde afuera para poder llenarse así. Esto aplica al conteo por posición y a "Contar por producto".
- Opcional (no pedido): lo mismo en otras pantallas con lista de candidatos.

## 4. Pantalla principal y legibilidad
- **Problema:** hay que cambiar la resolución del aparato para ver todos los botones; la pantalla principal apila cinco botones grandes, Sincronizar y Cerrar sesión **sin desplazamiento** (no hay `ScrollView`).
- **Cambios:**
  1. Botones de las acciones en **dos columnas** y **más altos** (alrededor de 88 dp, con icono arriba y texto debajo), de modo que quepan todos sin tocar la resolución.
  2. La pantalla principal se vuelve desplazable como red de seguridad (por si el aparato tiene una pantalla más corta).
  3. **Letras más grandes** en todo el kit (`LineList`, mensajes, ayuda y errores): títulos de lista 16 → 18, subtítulos 13 → 15, mensajes de ayuda y de error 13 → 16 (mínimo 16 en cualquier mensaje), textos de sincronización 14 → 16, etiqueta de los botones 18 → 20.
  4. Los **mensajes que salen al escanear** (aviso de producto o posición encontrada o no encontrada) pasan a un bloque más grande, con color y contraste altos, y se quedan en pantalla lo suficiente para leerlos.
- **Prueba:** capturas en un perfil de pantalla de 4" (como el Zebra) y en uno más grande; que ningún texto se corte con la fuente grande del sistema.

## Pruebas y entrega
- Pruebas unitarias y de pantalla de la app (jest) para cada punto; `lint`, `tsc` y el APK por el CI.
- Documento de decisiones del lote y capítulo del manual de la app (`docs/manual/09-app-almacen.md`) con la lista de comprobación para el Zebra.
- No toca el servidor ni la web.
