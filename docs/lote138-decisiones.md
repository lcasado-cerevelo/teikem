# Lote 138 — Marca con tres colores por elemento y paleta de Advance Logistics por defecto (pedido de Luis, 2026-10-19)

## Qué se construyó
- **Tema por defecto = la página de Advance Logistics** (leída de advancelogisticspr.com): **azul marino `#03204B`** (de ahí salen fondos, paneles y líneas: tono 216), **naranja `#F37123`** (botones, enlaces, navegación y foco) y **gris `#6C757D`** (marcas, contadores y dinero). Es el tema `advance`, primero de la lista y `DefaultPreset` (C# `BrandingRules` y web `brandTheme.ts`); el azul y naranja de antes sigue como tema `teikem` («Teikem (azul y naranja)»). `tokens.css` trae esa paleta como base, idéntica a lo que calcula el tema (una prueba lo comprueba, oscuro y claro).
- **Tres roles por elemento** en vez de dos por módulo: **Primario** (`custom.flow`: barras, navegación, pestañas), **Acción** (`custom.action`, opcional, por omisión el primario: botones, enlaces, foco y selección) y **Acento** (`custom.money`: chips, marcas, contadores y el dinero). Variables CSS nuevas `--action*` y `--accent*` (por omisión apuntan a `--flow*` y `--money*`: las marcas guardadas se ven igual hasta que se toque el color de acción). `base.css`/`ui.css` repartidos: botón principal, enlaces, foco y casillas → acción; `.tag`, contador del selector múltiple, `.btn.money`, montos (`.cash`) y estatus COD → acento.
- **Texto legible sobre cada color de marca** (hallazgo): el blanco sobre naranja da 2.9:1 (< AA 4.5:1). Tokens `--on-flow`, `--on-action`, `--on-action-2`, `--on-accent` que el tema calcula solo (`onColor`: blanco si llega a 4.5:1; si no, azul marino oscuro `#0A1B33` o blanco, el que dé más contraste). Los 20 `color:#fff` fijos sobre fondos de marca en los CSS pasaron a esos tokens; el avatar del usuario es de color sólido.
- **Validación:** el color de acción propio se comprueba como los demás (≥ 4.5:1 contra el panel, dos modos) solo cuando es distinto del primario (los 9 cheques de antes no cambian); mensaje `El contraste del color de acción en modo <oscuro|claro> es …`. Mismo código en C# y web; vectores compartidos regenerados con +4 casos (tema `advance` y tres de color de acción).
- **Ajustes → Marca:** rótulos «Color primario», «Color de acción» (con «Usar el mismo que el primario»), «Color de acento», muestra con barra, botón, marca y monto, y los temas muestran tres cuadros de color.

## Cómo se probó
- xunit (Release, una corrida, prioridad baja): `BrandingRulesTests`, `TenantBrandSettingsTests`, `BrandLogoTests`: 119 pasan (paridad con los vectores, 14 temas, acción opcional/distinta/inválida).
- vitest: `kernel/ui`, `features/system` y `app` (69 archivos, 637 pruebas) y `tsc --noEmit` limpios; incluye la prueba de que `tokens.css` = tema Advance calculado y la de `onColor`.
- No se corrió Playwright ni se revisó la pantalla en el navegador (se ajustó el número del recorrido `loteF11-marca.spec.ts`: 1.34 → 1.33 por el nuevo neutro).

## Decisiones a revisar
1. **Texto sobre el naranja:** en su página los botones llevan letras blancas (2.9:1, no pasa AA); en Teikem lo hice azul marino oscuro para cumplir la regla de contraste. Si prefieres el blanco como en su página, es cambiar `onColor` (y se pierde AA).
2. **El azul marino solo tiñe fondos y texto;** no hay todavía un «botón secundario azul marino» como el «Track order» de su página (sería un estilo de botón nuevo).
3. **Las marcas guardadas siguen igual** (el tema `teikem` se conserva y los colores propios no cambian); solo las compañías sin marca guardada pasan al tema Advance.
4. Las apps móviles (`app-cliente`, `app-almacen`) tienen su propia paleta: no se tocaron.
