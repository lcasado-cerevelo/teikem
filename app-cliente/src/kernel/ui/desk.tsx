// Piezas de la versión de ESCRITORIO del portal (2026-10-22, pedido de Luis: «se ve bien tecato en la web»): encabezado de página con acciones, fichas de indicadores y una tabla de datos
// con encabezado fijo, filas que se iluminan al pasar el ratón y columnas alineables. El celular sigue con tarjetas; estas piezas se usan solo cuando `useIsWide()` es verdadero.
import { useState, type ReactNode } from 'react'
import { ActivityIndicator, Pressable, Text, View, type StyleProp, type ViewStyle } from 'react-native'

import { useT } from '../i18n/useT'
import { exportFormats, type CsvValue, type ExportFormat } from './tableExport'
import { useColors } from './theme'

/** Título de la página con su subtítulo a la izquierda y las acciones (botones, filtros) a la derecha. */
export function PageHeader({ title, subtitle, children }: { title: string; subtitle?: string; children?: ReactNode }) {
  const c = useColors()
  return (
    <View style={{ flexDirection: 'row', alignItems: 'flex-end', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap', paddingBottom: 4 }}>
      <View style={{ gap: 2, flexShrink: 1 }}>
        <Text accessibilityRole="header" style={{ fontSize: 26, fontWeight: '800', color: c.text }}>{title}</Text>
        {!!subtitle && <Text style={{ fontSize: 14, color: c.muted }}>{subtitle}</Text>}
      </View>
      {!!children && <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>{children}</View>}
    </View>
  )
}

/** Ficha de un indicador: número grande y su etiqueta; `tone` pinta el número (por omisión el color de la marca). */
export function Kpi({ label, value, hint, tone, testID }: { label: string; value: string; hint?: string; tone?: 'ok' | 'warn' | 'danger'; testID?: string }) {
  const c = useColors()
  const color = tone === 'ok' ? c.ok : tone === 'warn' ? c.warn : tone === 'danger' ? c.danger : c.done
  return (
    <View style={{ flexGrow: 1, flexBasis: 180, minWidth: 160, backgroundColor: c.card, borderRadius: 12, borderWidth: 1, borderColor: c.line, padding: 16, gap: 4 }}>
      <Text style={{ fontSize: 12, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: c.muted }}>{label}</Text>
      <Text testID={testID} style={{ fontSize: 30, fontWeight: '800', color }}>{value}</Text>
      {!!hint && <Text style={{ fontSize: 12, color: c.muted }}>{hint}</Text>}
    </View>
  )
}

export function KpiRow({ children }: { children: ReactNode }) {
  return <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 12 }}>{children}</View>
}

export interface Column<T> {
  key: string
  title: string
  /** Ancho relativo (por omisión 1); `width` fija el ancho en píxeles. */
  flex?: number
  width?: number
  align?: 'left' | 'right' | 'center'
  render: (row: T) => ReactNode
  /** Valor que sale al exportar (2026-10-23); sin él la columna no se exporta (acciones). */
  csv?: (row: T) => CsvValue
}

/** «Exportar» de una tabla o lista (2026-10-23, pedido de Luis: como en la web del personal, se exporta TODO lo filtrado y en varios formatos): Excel, CSV y PDF en la web; solo CSV en el teléfono. `onExport` trae las filas y baja el archivo. */
export function ExportButton({ onExport, testID = 'export', link, label }: { onExport: (format: ExportFormat) => Promise<void>; testID?: string; link?: boolean; label?: string }) {
  const c = useColors()
  const { t } = useT()
  const [busy, setBusy] = useState<ExportFormat | null>(null)
  const [error, setError] = useState<string | null>(null)
  const run = async (format: ExportFormat) => {
    if (busy) return
    setBusy(format)
    setError(null)
    try {
      await onExport(format)
    } catch (e) {
      setError(e instanceof Error && e.message ? e.message : t('errors.generic'))
    } finally {
      setBusy(null)
    }
  }
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 10, flexWrap: 'wrap' }} testID={testID}>
      <Text style={{ fontSize: link ? 12 : 13, fontWeight: '700', color: c.muted }}>{label ?? t('table.export')}</Text>
      {exportFormats().map((f) => (
        <Pressable key={f} accessibilityRole="button" accessibilityLabel={`${t('table.export')} ${t(`table.format.${f}`)}`} accessibilityState={{ busy: busy === f, disabled: !!busy }} testID={`${testID}-${f}`} disabled={!!busy} onPress={() => void run(f)}
          style={link ? { paddingVertical: 2, paddingHorizontal: 2, flexDirection: 'row', gap: 4, alignItems: 'center' } : { minHeight: 36, borderRadius: 10, borderWidth: 1, borderColor: c.secondaryLine, paddingHorizontal: 14, alignItems: 'center', justifyContent: 'center', flexDirection: 'row', gap: 6, backgroundColor: c.card }}>
          {busy === f && <ActivityIndicator size="small" color={c.link} />}
          <Text style={{ color: c.link, fontWeight: '700', fontSize: link ? 12 : 14, textDecorationLine: link ? 'underline' : 'none' }}>{t(`table.format.${f}`)}</Text>
        </Pressable>
      ))}
      {!!error && <Text accessibilityRole="alert" style={{ fontSize: 12, color: c.danger }}>{error}</Text>}
    </View>
  )
}

/** Tabla de datos: encabezado, filas con rayado suave y, si hay `onRowPress`, filas que se pueden abrir. Con `total` dice abajo cuántas filas se ven de cuántas hay (2026-10-23, pedido de Luis: toda tabla dice su total). */
export function DataTable<T>({ columns, rows, keyOf, onRowPress, empty, testID, style, total, onExport }: {
  columns: Column<T>[]
  rows: T[]
  keyOf: (row: T) => string
  onRowPress?: (row: T) => void
  empty?: string
  testID?: string
  style?: StyleProp<ViewStyle>
  /** Total de filas que cumplen el filtro (no solo las que se ven). */
  total?: number
  /** Exportar todo lo filtrado (Excel, CSV o PDF); pinta «Exportar» en el pie de la tabla. */
  onExport?: (format: ExportFormat) => Promise<void>
}) {
  const c = useColors()
  const { t } = useT()
  const cell = (col: Column<T>): ViewStyle => ({ flex: col.width ? undefined : (col.flex ?? 1), width: col.width, justifyContent: 'center', alignItems: col.align === 'right' ? 'flex-end' : col.align === 'center' ? 'center' : 'flex-start', paddingHorizontal: 10, minWidth: 0 })
  return (
    <View testID={testID} style={[{ backgroundColor: c.card, borderRadius: 12, borderWidth: 1, borderColor: c.line, overflow: 'hidden' }, style]}>
      <View style={{ flexDirection: 'row', backgroundColor: c.neutralBg, paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: c.line }}>
        {columns.map((col) => (
          <View key={col.key} style={cell(col)}>
            <Text numberOfLines={1} style={{ fontSize: 12, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: c.muted }}>{col.title}</Text>
          </View>
        ))}
      </View>
      {rows.length === 0 && !!empty && <Text style={{ padding: 16, color: c.muted }}>{empty}</Text>}
      {rows.map((row, i) => {
        const rowBase = { flexDirection: 'row', minHeight: 46, paddingVertical: 8, alignItems: 'center', borderBottomWidth: i === rows.length - 1 ? 0 : 1, borderBottomColor: c.line } as const
        const cells = columns.map((col) => (
          <View key={col.key} style={cell(col)}>
            {col.render(row)}
          </View>
        ))
        // sin acción por fila la fila es una vista normal: un Pressable deshabilitado marca como deshabilitado todo lo de adentro (p. ej. el botón «Editar» de la fila)
        if (!onRowPress)
          return (
            <View key={keyOf(row)} style={{ ...rowBase, backgroundColor: c.card }}>
              {cells}
            </View>
          )
        return (
          <Pressable
            key={keyOf(row)}
            accessibilityRole="button"
            onPress={() => onRowPress(row)}
            style={({ pressed, hovered }: { pressed: boolean; hovered?: boolean }) => ({ ...rowBase, backgroundColor: pressed || hovered ? c.neutralBg : c.card })}
          >
            {cells}
          </Pressable>
        )
      })}
      {(total !== undefined || onExport) && (
        <View style={{ paddingVertical: 6, paddingHorizontal: 10, borderTopWidth: 1, borderTopColor: c.line, backgroundColor: c.neutralBg, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <Text testID={testID ? `${testID}-count` : undefined} style={{ fontSize: 12, fontWeight: '600', color: c.muted }}>{total !== undefined ? t('table.count', { shown: rows.length, total }) : ''}</Text>
          {onExport && <ExportButton link onExport={onExport} testID={testID ? `${testID}-export` : 'export'} />}
        </View>
      )}
    </View>
  )
}

/** Texto de una celda con el estilo normal (para `render`). */
export function CellText({ children, bold, muted, align }: { children: ReactNode; bold?: boolean; muted?: boolean; align?: 'right' | 'left' }) {
  const c = useColors()
  return <Text numberOfLines={2} style={{ fontSize: 14, fontWeight: bold ? '700' : '400', color: muted ? c.muted : c.text, textAlign: align ?? 'left' }}>{children}</Text>
}
