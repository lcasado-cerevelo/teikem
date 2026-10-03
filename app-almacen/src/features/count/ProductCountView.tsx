// Lote A4 — lista del conteo por producto (docs/conteo-por-producto-diseno.md, "App móvil"): una fila por posición (y lote)
// con un espacio para la cantidad. Reglas del dueño: en blanco = 0; Confirmar cierra y, en el mismo toque, una línea de resumen
// dice cuántas posiciones en blanco se toman como 0 (no hay diálogo ni paso extra); no hay "todo aquí"; el buscador aparece
// solo con más de PRODUCT_COUNT_SEARCH_THRESHOLD posiciones. Las cantidades esperadas se muestran solo si el servidor las trajo.
// Cada cambio se guarda en la base local (se puede cerrar la app y retomar, sin señal).
// Lote A5 (decisión del dueño 4): Confirmar exige al menos una posición con un número escrito (0 vale); con todo en blanco no
// se manda nada y sale el aviso grande (ScanMessage) encima de Confirmar.
import { useMemo, useState } from 'react'
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { ScanMessage } from '../../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing, touchTarget } from '../../kernel/ui/theme'
import type { CreatedBin } from './countApi'
import {
  canConfirmProductCount,
  filterProductRows,
  hasNothingToConfirm,
  parseQty,
  productCountConfirmBlock,
  showProductSearch,
  summarizeProductCount,
} from './countLogic'
import {
  addProductExtraRow,
  getProductCountRows,
  removeLocalCountLine,
  setProductRowQty,
  type OpenCount,
  type ProductCountRow,
} from './localCount'
import { OtherBinForm, type OtherBinLot } from './OtherBinForm'

export interface ProductCountViewProps {
  openCount: OpenCount
  busy: boolean
  /** Confirmar: encola el lote de captura y el cierre (la pantalla lo hace con lo guardado en la base local). */
  onConfirm: () => void
  onCancelCount: () => void
  /** Error de la pantalla (p. ej. no se pudo cancelar sin señal). */
  error?: string | null
}

function textOf(row: ProductCountRow): string {
  return row.countedQty == null ? '' : String(row.countedQty)
}

export function ProductCountView({ openCount, busy, onConfirm, onCancelCount, error }: ProductCountViewProps) {
  const { t } = useT()
  const product = openCount.product!
  const [tick, setTick] = useState(0)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const rows = useMemo(() => getProductCountRows(openCount.id), [openCount.id, tick])
  // Lo escrito en cada espacio (por id de fila). Lo que no está aquí se toma de la base (lo guardado antes de cerrar la app).
  const [texts, setTexts] = useState<Record<number, string>>({})
  const [query, setQuery] = useState('')
  const [other, setOther] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [emptyWarning, setEmptyWarning] = useState(false)
  // aviso de "todo en blanco" tras tocar Confirmar; se quita al escribir una cantidad o agregar una posición
  const [blankWarning, setBlankWarning] = useState(false)

  const textFor = (row: ProductCountRow) => texts[row.id] ?? textOf(row)
  const summary = summarizeProductCount(rows.map(textFor))
  const searchable = showProductSearch(rows.length)
  const visible = searchable ? filterProductRows(rows, query) : rows

  function changeQty(row: ProductCountRow, value: string) {
    setTexts((prev) => ({ ...prev, [row.id]: value }))
    setNotice(null)
    setBlankWarning(false)
    if (value.trim() === '') setProductRowQty(row.id, null)
    else {
      const qty = parseQty(value)
      // lo que no es una cantidad no se guarda (y no deja confirmar hasta corregirlo)
      if (qty !== null) setProductRowQty(row.id, qty)
    }
  }

  const empty = hasNothingToConfirm(summary)

  function openOther() {
    setNotice(null)
    setEmptyWarning(false)
    setBlankWarning(false)
    setOther(true)
  }

  /** Sin ninguna fila no hay nada que mandar: el servidor no termina un conteo vacío. Se avisa y se puede cancelar.
   *  Con filas pero todas en blanco tampoco se manda nada: hace falta al menos un número (0 si no hay nada). */
  function confirm() {
    const block = productCountConfirmBlock(summary)
    if (block === 'empty') {
      setEmptyWarning(true)
      return
    }
    if (block === 'allBlank') {
      setNotice(null)
      setBlankWarning(true)
      return
    }
    if (block === 'invalid') return
    onConfirm()
  }

  function added(bin: CreatedBin, lot: OtherBinLot | null, existing: boolean) {
    addProductExtraRow(openCount.id, { publicId: product.publicId, sku: product.sku, name: product.name }, bin, lot)
    setOther(false)
    setEmptyWarning(false)
    setBlankWarning(false)
    setQuery('')
    setNotice(existing ? t('count.binExistingUsed', { bin: bin.code }) : t('count.binAdded', { bin: bin.code }))
    setTick((n) => n + 1)
  }

  if (other) {
    return (
      <OtherBinForm
        warehousePublicId={openCount.warehousePublicId}
        countId={openCount.countId}
        product={product}
        rows={rows}
        onAdded={added}
        onCancel={() => setOther(false)}
      />
    )
  }

  const summaryText = summary.invalid > 0 ? null : summary.blanks === 1 ? t('count.blankSummaryOne') : summary.blanks > 1 ? t('count.blankSummaryMany', { count: summary.blanks }) : null

  return (
    <View style={styles.wrap}>
      <Text style={styles.title}>{product.name}</Text>
      <Text style={styles.help}>{product.sku}</Text>
      {openCount.isBlind ? <Text style={styles.help}>{t('count.blindNotice')}</Text> : null}
      {product.trackingTypeCode === 'LOT' ? <Text style={styles.help}>{t('count.lotNotice')}</Text> : null}
      <Text style={styles.help}>{t('count.blankRule')}</Text>

      {searchable ? (
        <View style={styles.field}>
          <Text style={styles.label}>{t('count.searchLabel')}</Text>
          <TextInput
            value={query}
            onChangeText={setQuery}
            style={styles.search}
            accessibilityLabel={t('count.searchLabel')}
            autoCapitalize="characters"
            autoCorrect={false}
          />
        </View>
      ) : null}

      <View>
        {visible.map((row) => {
          const parts = [
            row.lotNumber ? t('count.rowLot', { lot: row.lotNumber }) : null,
            row.isProvisionalBin ? t('count.rowProvisional') : null,
            !row.isExtra && row.systemQty != null ? t('count.expectedQtyLabel', { qty: row.systemQty }) : null,
          ].filter(Boolean)
          const text = textFor(row)
          const invalid = text.trim() !== '' && parseQty(text) === null
          const qtyLabel = row.lotNumber ? t('count.qtyAtLot', { bin: row.binCode, lot: row.lotNumber }) : t('count.qtyAt', { bin: row.binCode })
          return (
            <View key={row.id} style={styles.row} testID={`count-row-${row.binCode}`}>
              <View style={styles.texts}>
                <Text style={styles.rowTitle}>{row.binCode}</Text>
                {parts.length > 0 ? <Text style={styles.rowSubtitle}>{parts.join(' · ')}</Text> : null}
              </View>
              <TextInput
                value={text}
                onChangeText={(v) => changeQty(row, v)}
                keyboardType="decimal-pad"
                style={[styles.qty, invalid && styles.qtyInvalid]}
                accessibilityLabel={qtyLabel}
                placeholder="0"
                placeholderTextColor={colors.muted}
                selectTextOnFocus
              />
              {row.isExtra ? (
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={t('count.removeRow', { bin: row.binCode })}
                  onPress={() => {
                    removeLocalCountLine(row.id)
                    setTick((n) => n + 1)
                  }}
                  style={styles.removeBtn}
                >
                  <Text style={styles.removeLabel}>✕</Text>
                </Pressable>
              ) : null}
            </View>
          )
        })}
        {searchable && visible.length === 0 ? <Text style={styles.help}>{t('count.searchEmpty', { query: query.trim() })}</Text> : null}
      </View>

      <ScanMessage tone="ok" message={notice} />
      {empty ? (
        // el sistema no tiene existencia de este producto: lo único que se puede hacer es registrar dónde se encontró
        <View style={styles.emptyBlock} testID="count-empty-block">
          <Text style={styles.emptyText}>{t('count.noStockEmpty')}</Text>
          <BigButton label={t('count.otherBin')} onPress={openOther} disabled={busy} />
        </View>
      ) : (
        <Pressable accessibilityRole="button" accessibilityLabel={t('count.otherBin')} onPress={openOther} disabled={busy} style={styles.link}>
          <Text style={styles.linkLabel}>{t('count.otherBin')}</Text>
        </Pressable>
      )}

      {summary.invalid > 0 ? <Text style={styles.error}>{t('count.invalidQty')}</Text> : null}
      {summaryText ? <Text style={styles.summary}>{summaryText}</Text> : null}
      {emptyWarning && empty ? <Text style={styles.error}>{t('count.confirmEmpty')}</Text> : null}
      <ScanMessage tone="error" message={blankWarning && summary.filled === 0 && !empty ? t('count.confirmAllBlank') : null} />
      <BigButton label={t('count.confirmProduct')} onPress={confirm} disabled={!canConfirmProductCount(summary) || busy} />
      <Text style={styles.help}>{t('count.finishHelp')}</Text>
      <ScanMessage tone="error" message={error} />
      <BigButton label={t('count.cancelCount')} variant="danger" onPress={onCancelCount} disabled={busy} />
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message, fontWeight: '700' },
  summary: { color: colors.warn, fontSize: fontSize.label, fontWeight: '700' },
  field: { gap: spacing.xs },
  search: {
    minHeight: touchTarget,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.panelAlt,
    borderRadius: radius.md,
    marginBottom: spacing.sm,
  },
  // el texto se envuelve si el código es largo; nunca empuja el espacio de la cantidad fuera de la pantalla (360 px)
  texts: { flex: 1, minWidth: 0, gap: 2 },
  rowTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  rowSubtitle: { color: colors.muted, fontSize: fontSize.listSubtitle },
  qty: {
    width: 96,
    minHeight: touchTarget,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.sm,
    fontSize: 22,
    fontWeight: '700',
    textAlign: 'right',
    color: colors.text,
    backgroundColor: colors.bg,
  },
  qtyInvalid: { borderColor: colors.error },
  removeBtn: {
    minWidth: 44,
    minHeight: 44,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: colors.line,
    borderRadius: radius.sm,
  },
  removeLabel: { color: colors.error, fontSize: 22, fontWeight: '700' },
  emptyBlock: { gap: spacing.md, padding: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.warn, backgroundColor: colors.panelAlt },
  emptyText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  link: { minHeight: 48, justifyContent: 'center', alignSelf: 'flex-start', paddingHorizontal: spacing.xs },
  linkLabel: { color: colors.brand, fontSize: fontSize.label, fontWeight: '700', textDecorationLine: 'underline' },
})
