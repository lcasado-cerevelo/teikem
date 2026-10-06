// Lote 24 — conteo abierto con varios productos ("Por producto", pedido del dueño 2026-10-05): se abre UN conteo y se van
// escaneando productos; cada uno entra con su cantidad y su posición, que es opcional: la app propone la única posición donde
// el sistema dice que está (GET /cycle-counts/{id}/product-bins, sin cantidades: sirve también a ciegas), con la opción de
// cambiarla (elegir otra de la lista, escanear otra o crear una nueva). Al final se manda UN lote con todas las líneas y el
// cierre por la cola de salida. Escanear otra vez un producto en la misma posición abre la línea ya contada para corregirla.
// Sin señal no se puede buscar la posición del producto: se pide escanearla (la tabla de posiciones es local).
import { useEffect, useMemo, useState } from 'react'
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'

import { ApiError, apiErrorMessage, isNetworkError } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { LineList } from '../../kernel/ui/LineList'
import { ScanField } from '../../kernel/ui/ScanField'
import { ScanMessage } from '../../kernel/ui/ScanMessage'
import { vibrateError, vibrateOk } from '../../kernel/ui/feedback'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { findBinByCode } from '../../kernel/warehouse/binLookup'
import { findProductByCode } from '../../kernel/warehouse/productLookup'
import { fetchProductBins, findLocalBin, type CreatedBin } from './countApi'
import { binOptionLabel, defaultBinOption, findListedRow, parseQty, productCountBlocker, type BinOption } from './countLogic'
import { addOpenCountLine, getProductCountRows, removeLocalCountLine, updateLocalCountLineQty, type CountProduct, type OpenCount } from './localCount'
import { OtherBinForm, type OtherBinLot } from './OtherBinForm'
import { QuantityField } from './QuantityField'
import { KeyboardInput } from '../../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../../kernel/ui/KeyboardScreen'

export interface OpenCountViewProps {
  openCount: OpenCount
  busy: boolean
  /** Terminar: encola el lote con todas las líneas y el cierre (la pantalla lo hace con lo guardado en la base local). */
  onConfirm: () => void
  onCancelCount: () => void
  /** Producto cuyo escaneo abrió el conteo: se procesa al montar, como si se hubiera escaneado ya dentro. */
  initialProduct?: CountProduct | null
  error?: string | null
}

interface Draft {
  product: CountProduct
  /** Dónde dice el sistema que está (posición y lote con existencia); sin cantidades. */
  options: BinOption[]
  choice: BinOption | null
  lotText: string
  qtyText: string
  /** Fila ya contada que se está corrigiendo (solo cambia la cantidad). */
  editingRowId: number | null
  /** Sin señal al buscar dónde está: hay que escanear la posición. */
  offline: boolean
  picking: boolean
  other: boolean
  lotExpiry: string | null
  isProvisional: boolean
  message: string | null
}

export function OpenCountView({ openCount, busy, onConfirm, onCancelCount, initialProduct, error }: OpenCountViewProps) {
  const { t } = useT()
  const [tick, setTick] = useState(0)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const rows = useMemo(() => getProductCountRows(openCount.id), [openCount.id, tick])
  const [draft, setDraft] = useState<Draft | null>(null)
  const [scanError, setScanError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const refresh = () => setTick((n) => n + 1)

  function newDraft(product: CountProduct, options: BinOption[], offline: boolean): Draft {
    return {
      product,
      options,
      choice: defaultBinOption(options),
      lotText: '',
      qtyText: '',
      editingRowId: null,
      offline,
      picking: false,
      other: false,
      lotExpiry: null,
      isProvisional: false,
      message: null,
    }
  }

  /** Producto escaneado: dónde está (en línea) → propuesta de posición; una fila igual ya contada se abre para corregir. */
  async function scanProduct(code: string) {
    setScanError(null)
    setNotice(null)
    const found = findProductByCode(code)
    if (!found) {
      setScanError(t('count.productNotFound'))
      vibrateError()
      return
    }
    if (productCountBlocker(found) === 'serial') {
      setScanError(t('count.serialNotSupported'))
      vibrateError()
      return
    }
    await openProduct({ publicId: found.publicId, sku: found.sku, name: found.name, trackingTypeCode: found.trackingTypeCode })
  }

  async function openProduct(product: CountProduct) {
    setLoading(true)
    try {
      let options: BinOption[] = []
      let offline = false
      try {
        options = await fetchProductBins(openCount.countId, product.publicId)
      } catch (err) {
        if (isNetworkError(err)) offline = true
        else if (err instanceof ApiError) {
          setScanError(apiErrorMessage(err))
          vibrateError()
          return
        } else throw err
      }
      const next = newDraft(product, options, offline)
      // la propuesta ya está contada: se abre esa línea para corregir la cantidad (no se duplica ni se suma)
      const proposed = next.choice
      const listed = proposed ? findListedRow(rows.filter((r) => r.productPublicId === product.publicId), proposed.binCode, proposed.lotNumber) : null
      if (listed) {
        setDraft({ ...next, editingRowId: listed.id, qtyText: listed.countedQty == null ? '' : String(listed.countedQty), message: t('count.openEditing', { sku: product.sku, bin: listed.binCode }) })
      } else setDraft(next)
      vibrateOk()
    } finally {
      setLoading(false)
    }
  }

  // El conteo se acaba de abrir con este producto: se procesa una sola vez al montar.
  useEffect(() => {
    if (initialProduct) void openProduct(initialProduct)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function choose(option: BinOption) {
    setDraft((d) => (d ? { ...d, choice: option, picking: false, message: null, lotText: '' } : d))
  }

  async function scanOtherBin(code: string) {
    if (!draft) return
    try {
      // primero la copia sincronizada del aparato (sin señal); si no está, el servidor
      const bin = findLocalBin(openCount.warehousePublicId, code) ?? (await findBinByCode(openCount.warehousePublicId, code))
      if (!bin) {
        setDraft((d) => (d ? { ...d, message: t('count.binNotFound') } : d))
        vibrateError()
        return
      }
      setDraft((d) =>
        d ? { ...d, choice: { binId: bin.id, binCode: bin.code, zoneCode: '', lotId: null, lotNumber: null }, picking: false, message: null, lotText: '' } : d,
      )
      vibrateOk()
    } catch {
      setDraft((d) => (d ? { ...d, message: t('count.binNotFound') } : d))
    }
  }

  function otherAdded(bin: CreatedBin, lot: OtherBinLot | null) {
    setDraft((d) =>
      d
        ? {
            ...d,
            choice: { binId: bin.id, binCode: bin.code, zoneCode: '', lotId: null, lotNumber: lot?.number ?? null },
            lotExpiry: lot?.expiryDate ?? null,
            isProvisional: bin.isProvisional,
            other: false,
            picking: false,
            message: null,
          }
        : d,
    )
  }

  function save() {
    if (!draft) return
    const qty = parseQty(draft.qtyText)
    if (qty === null) return
    if (draft.editingRowId !== null) {
      updateLocalCountLineQty(draft.editingRowId, qty)
      setDraft(null)
      setNotice(t('count.openUpdated', { sku: draft.product.sku }))
      refresh()
      vibrateOk()
      return
    }
    if (!draft.choice) return
    const lotNumber = (draft.choice.lotNumber ?? draft.lotText.trim()) || null
    if (draft.product.trackingTypeCode === 'LOT' && !lotNumber) return
    const dup = findListedRow(rows.filter((r) => r.productPublicId === draft.product.publicId), draft.choice.binCode, lotNumber)
    if (dup) {
      setDraft({ ...draft, message: t('count.openAlreadyListed', { sku: draft.product.sku, bin: draft.choice.binCode }) })
      vibrateError()
      return
    }
    addOpenCountLine(
      openCount.id,
      { publicId: draft.product.publicId, sku: draft.product.sku, name: draft.product.name },
      { id: draft.choice.binId, code: draft.choice.binCode, isProvisional: draft.isProvisional },
      lotNumber ? { id: draft.choice.lotId, number: lotNumber, expiryDate: draft.lotExpiry } : null,
      qty,
    )
    setNotice(t('count.openAdded', { sku: draft.product.sku, bin: draft.choice.binCode }))
    setDraft(null)
    refresh()
    vibrateOk()
  }

  // ------------------------------------------------------------------ "Otra posición" (nueva) dentro del producto
  if (draft?.other) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <OtherBinForm
          warehousePublicId={openCount.warehousePublicId}
          countId={openCount.countId}
          product={draft.product}
          rows={rows.filter((r) => r.productPublicId === draft.product.publicId)}
          onAdded={otherAdded}
          onCancel={() => setDraft((d) => (d ? { ...d, other: false } : d))}
        />
      </KeyboardScreen>
    )
  }

  // ------------------------------------------------------------------ producto escaneado: posición y cantidad
  if (draft) {
    const qty = parseQty(draft.qtyText)
    const isLot = draft.product.trackingTypeCode === 'LOT'
    const needsLotText = isLot && draft.choice !== null && !draft.choice.lotNumber && draft.editingRowId === null
    const lotMissing = needsLotText && draft.lotText.trim() === ''
    const canSave = qty !== null && (draft.editingRowId !== null || (draft.choice !== null && !lotMissing))
    const editingRow = draft.editingRowId !== null ? rows.find((r) => r.id === draft.editingRowId) : null
    const shownBin = editingRow ? editingRow.binCode : draft.choice?.binCode ?? null
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{draft.product.name}</Text>
        <Text style={styles.help}>{draft.product.sku}</Text>
        {openCount.isBlind ? <Text style={styles.help}>{t('count.blindNotice')}</Text> : null}

        {shownBin ? (
          <View style={styles.binBox} testID="open-count-bin">
            <Text style={styles.binLabel}>{t('count.openBinLabel')}</Text>
            <Text style={styles.binCode}>{shownBin}</Text>
            {draft.choice?.lotNumber || editingRow?.lotNumber ? <Text style={styles.help}>{t('count.rowLot', { lot: (draft.choice?.lotNumber ?? editingRow?.lotNumber) as string })}</Text> : null}
            {draft.editingRowId === null ? (
              <Pressable accessibilityRole="button" accessibilityLabel={t('count.openChangeBin')} onPress={() => setDraft({ ...draft, picking: true })} style={styles.link}>
                <Text style={styles.linkLabel}>{t('count.openChangeBin')}</Text>
              </Pressable>
            ) : null}
          </View>
        ) : (
          <Text style={styles.warn}>{draft.offline ? t('count.openBinOffline') : draft.options.length > 1 ? t('count.openChooseBin') : t('count.openNoStock')}</Text>
        )}

        {/* varias posiciones, o cambiar la propuesta: se elige una, se escanea otra o se crea una nueva */}
        {draft.editingRowId === null && (draft.picking || (!draft.choice && draft.options.length > 1)) ? (
          <View style={styles.pick}>
            {draft.picking && draft.options.length > 0 ? <Text style={styles.label}>{t('count.openChooseBin')}</Text> : null}
            {draft.options.map((o) => (
              <BigButton key={`${o.binId}-${o.lotId ?? 0}`} label={binOptionLabel(o, t)} variant="secondary" onPress={() => choose(o)} />
            ))}
          </View>
        ) : null}
        {draft.editingRowId === null && (draft.picking || !draft.choice) ? (
          <View style={styles.pick}>
            <ScanField key="other-bin" label={t('count.openScanOtherBin')} onSubmit={(c) => void scanOtherBin(c)} autoFocus={false} pick="bin" />
            <BigButton label={t('count.otherBin')} variant="secondary" onPress={() => setDraft({ ...draft, other: true })} />
          </View>
        ) : null}
        {draft.message ? <ScanMessage tone="error" message={draft.message} /> : null}

        {needsLotText ? (
          <View style={styles.field}>
            <Text style={styles.label}>{t('count.lotLabel')}</Text>
            <KeyboardInput
              value={draft.lotText}
              onChangeText={(v) => setDraft((d) => (d ? { ...d, lotText: v } : d))}
              style={styles.input}
              accessibilityLabel={t('count.lotLabel')}
              autoCapitalize="characters"
              autoCorrect={false}
            />
            {lotMissing ? <Text style={styles.help}>{t('count.lotRequired')}</Text> : null}
          </View>
        ) : null}

        <View style={styles.field}>
          <Text style={styles.label}>{t('count.foundQtyLabel')}</Text>
          <QuantityField
            value={draft.qtyText}
            onChangeText={(v) => setDraft((d) => (d ? { ...d, qtyText: v, message: null } : d))}
            style={styles.input}
            accessibilityLabel={t('count.foundQtyLabel')}
            autoFocus
            selectTextOnFocus
          />
        </View>
        <View style={styles.row}>
          <View style={styles.half}>
            <BigButton label={t('common.cancel')} variant="secondary" onPress={() => setDraft(null)} />
          </View>
          <View style={styles.half}>
            <BigButton label={draft.editingRowId !== null ? t('common.save') : t('count.addFound')} onPress={save} disabled={!canSave} />
          </View>
        </View>
      </KeyboardScreen>
    )
  }

  // ------------------------------------------------------------------ escaneando productos y viendo lo contado
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('count.title')}</Text>
      {openCount.isBlind ? <Text style={styles.help}>{t('count.blindNotice')}</Text> : null}
      <ScanField
        key="open-product"
        label={t('count.openScanLabel')}
        help={t('count.openScanHelp')}
        error={scanError}
        notice={notice}
        onSubmit={(c) => void scanProduct(c)}
        pick="product"
      />
      {loading ? <ActivityIndicator color={colors.brand} /> : null}

      <Text style={styles.label}>{t('count.openLinesTitle')}</Text>
      <LineList
        items={rows.map((r) => ({
          id: r.id,
          title: t('count.foundLineTitle', { name: r.productName, qty: r.countedQty ?? 0 }),
          subtitle: [r.sku, r.binCode, r.lotNumber ? t('count.rowLot', { lot: r.lotNumber }) : null].filter(Boolean).join(' · '),
        }))}
        onRemove={(id) => {
          removeLocalCountLine(Number(id))
          refresh()
        }}
        removeLabel={t('common.remove')}
        onEdit={(id) => {
          const row = rows.find((r) => r.id === Number(id))
          if (!row) return
          setNotice(null)
          setDraft({
            product: { publicId: row.productPublicId, sku: row.sku, name: row.productName, trackingTypeCode: row.lotNumber ? 'LOT' : 'NONE' },
            options: [],
            choice: null,
            lotText: '',
            qtyText: row.countedQty == null ? '' : String(row.countedQty),
            editingRowId: row.id,
            offline: false,
            picking: false,
            other: false,
            lotExpiry: null,
            isProvisional: row.isProvisionalBin,
            message: null,
          })
        }}
        editLabel={t('common.edit')}
        emptyLabel={t('count.openEmpty')}
      />

      <BigButton label={t('count.openFinish')} onPress={onConfirm} disabled={rows.length === 0 || busy} />
      <Text style={styles.help}>{t('count.finishHelp')}</Text>
      <ScanMessage tone="error" message={error} />
      <BigButton label={t('count.cancelCount')} variant="danger" onPress={onCancelCount} disabled={busy} />
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  warn: { color: colors.warn, fontSize: fontSize.label, fontWeight: '700' },
  field: { gap: spacing.xs },
  row: { flexDirection: 'row', gap: spacing.md },
  half: { flex: 1 },
  pick: { gap: spacing.sm },
  // la posición propuesta: grande, con "Cambiar posición" debajo (opcional)
  binBox: { gap: 2, padding: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  binLabel: { color: colors.muted, fontSize: fontSize.message },
  binCode: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  link: { minHeight: 48, justifyContent: 'center', alignSelf: 'flex-start', paddingHorizontal: spacing.xs },
  linkLabel: { color: colors.brand, fontSize: fontSize.label, fontWeight: '700', textDecorationLine: 'underline' },
  input: {
    minHeight: 56,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: 12,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
})

