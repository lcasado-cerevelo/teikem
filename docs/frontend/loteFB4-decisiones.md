# Lote F-B4 — Consignatarios en la ficha del cliente y Exportar (2026-10-14)

Pedido de Luis: un panel **Consignatarios** al final de la ficha del cliente (antes del historial de estatus), tabla de 10 filas, buscador simple, **Exportar**, **Importar** y **Nuevo**; y un **Exportar** en la pantalla Consignatarios que exporte lo filtrado (ajuste del mismo día: va en la cabecera, junto a Importar y Nuevo, en el orden Cliente · Exportar · Importar · Nuevo consignatario, no en el panel de la izquierda). Manual de pantallas: [fb4-consignatarios-en-la-ficha-del-cliente.md](../manual/frontend/fb4-consignatarios-en-la-ficha-del-cliente.md); FAQ «Lote F-B4». Sin cambios de servidor ni de base de datos.

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/…`) |
|---|---|---|
| Panel de la ficha | Tabla de a lo sumo 10 filas (Número, Nombre, Pueblo, Persona de contacto, Estado), contador con todas las filtradas, buscador, «Mostrando 10 de N» + «Ver todos en Consignatarios», clic en fila abre el consignatario. Solo los propios del cliente y activos. | `features/clients/ClientConsigneesPanel.tsx`, `ClientDetailPanel.tsx` (va después del portal y antes del historial) |
| Exportar | Excel / CSV / PDF con las filas filtradas (todas, no solo las 10) por el mismo motor de las tablas (`exportTable`). | `features/consignees/consigneeExport.ts` |
| Menú Exportar hacia abajo | `ExportMenu` gana `placement="down"` (en la cabecera de un panel el menú no puede abrir hacia arriba). | `kernel/ui/ExportMenu.tsx`, `ui.css` |
| Pantalla Consignatarios | Cabecera: filtro **Cliente**, **Exportar** (lo que la lista tiene a la vista), **Importar** y **Nuevo consignatario**, juntos y en ese orden (`ExportMenu` gana `size="md"` para verse como los demás botones); `?client=<id>` abre la lista ya filtrada por ese cliente. | `features/consignees/ConsigneeListScreen.tsx` |
| Textos | `clients.consignees.*` y `consignees.export.*` en es y en. | `kernel/i18n/{es,en}.json` |

## Cómo se probó

| Prueba | Resultado |
|---|---|
| vitest `ClientConsigneesPanel.test.tsx` (5) y `ConsigneeExport.test.tsx` (3), y las carpetas `features/clients`, `features/consignees`, `kernel/ui` | 666 en verde |
| `tsc`, oxlint | sin errores |
| Playwright `e2e/loteFB4-consignatarios-en-cliente.spec.ts` (escritorio y móvil a 360 px, API propio y base desechable) | 2 en verde: 10 de 12, buscador, descarga CSV solo de lo filtrado, Importar, Nuevo (12 → 13 sin salir de la ficha), Ver todos, Exportar de la lista, sin scroll horizontal |

## Decisiones tomadas por omisión (a revisar con Luis)

1. **Importar y Nuevo abren las ventanas en la propia ficha**, no navegan a la pantalla Consignatarios y vuelven. Es la misma ventana (mismo código) con el cliente ya escogido; al cerrar se está en la ficha y la tabla se refresca sola. Si Luis prefiere una navegación real de ida y vuelta, es un cambio chico.
2. **El panel muestra solo los consignatarios propios y activos** del cliente (no los compartidos ni los inactivos); la lista completa está a un clic con «Ver todos».
3. **Exportar exporta todas las filas filtradas**, no solo las 10 visibles.
4. **El archivo no lleva teléfono** (vive aparte, en puntos de contacto; incluirlo pide una llamada por consignatario).
5. **Sin el permiso `locations.read` el panel no se pinta** (no se muestra un error).
6. El menú de Exportar ofrece Excel, CSV y PDF, igual que las demás tablas.

## Fuera de este lote

- Filas ordenables en el panel (la tabla viene ordenada por nombre).
- Paginación dentro del panel (para eso está «Ver todos»).

## Ajuste (2026-10-14): paneles desplegables y tabla paginada

Pedido de Luis: los paneles de la ficha del cliente deben ser desplegables, con un **resumen** cuando están cerrados (le gusta el símbolo de cada panel: se conserva y la flecha va a su izquierda), y la tabla de consignatarios debe **paginarse ahí mismo**.

| Pieza | Qué hace | Dónde (`web-app/src/…`) |
|---|---|---|
| `Panel` desplegable | Props `collapsible`, `defaultCollapsed`, `summary` y `storageKey`: flecha en la cabecera (y clic en la parte libre de la cabecera), cerrado muestra el resumen y oculta cuerpo, acciones y pie; el cuerpo **sigue montado** (no se pierde lo escrito ni las consultas); recuerda abierto/cerrado en `localStorage` (`teikem.panel.<clave>`, con try/catch) solo si se le da `storageKey` (los paneles del cliente NO la usan: arrancan cerrados cada vez). Es opcional: el resto de las pantallas no cambia. | `kernel/ui/Panel.tsx`, `ui.css`, `ui.panel.*` en i18n |
| Ficha del cliente | Perfil, Teléfonos y correos, Personas de contacto, Numeración, Campos personalizados, Contratos, Usuarios de portal, Consignatarios e Historial de estatus, cada uno con su resumen (`clients.summary.*`). La cabecera del cliente no se cierra. | `features/clients/*Panel.tsx`, `features/contacts/ContactPointsPanel.tsx` (prop `storageKey`) |
| Tabla paginada | El panel Consignatarios usa la paginación local de `DataTable` (10 por página, selector «Filas por página»); se quita «Mostrando 10 de N / Ver todos». | `features/clients/ClientConsigneesPanel.tsx` |

Pruebas: `PanelCollapsible.test.tsx` (6) y las del panel de consignatarios; vitest de `features/clients`, `consignees`, `contacts` y `kernel/ui` en verde; `tsc -b`; Playwright `loteFB4` en escritorio y móvil (paneles cerrados con su resumen, se recuerdan al recargar, paginación).

Decisiones por omisión: (1) todos los paneles de la ficha arrancan **cerrados** cada vez (pedido de Luis, 2026-10-14; no se recuerda lo que el usuario abrió); (2) cerrado se esconden también los botones del panel (Nuevo, Importar, Exportar…); (3) los resúmenes son de una línea con conteos y el dato principal; (4) el panel Teléfonos y correos del cliente es el compartido `ContactPointsPanel`, que solo es desplegable si quien lo usa le pasa `storageKey` (órdenes y choferes no cambian).

## Ajuste (2026-10-14): barra para cambiar el ancho en Consignatarios

La pantalla Consignatarios usa `SplitPane` (como Clientes y otras): la lista arranca con 36 % del ancho (antes 280 px fijos; unos 80 px más, ≈ 10 caracteres) y se puede arrastrar entre 22 % y 60 % (mínimos 300 px la lista y 460 px la ficha); la posición se recuerda por navegador (`teikem.split.consignees`); bajo 900 px se apila sin barra. Texto accesible de la barra: `consignees.splitLabel`. Playwright `loteFB-consignatarios` (escritorio y móvil) y `loteFB4` en verde; además se corrigió el paso de la plantilla del recorrido `loteFB` (el editor de F-B2 arranca con tres columnas: se agregan la 4 y la 5).
