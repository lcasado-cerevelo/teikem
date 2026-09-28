import { useMemo, useState } from 'react'
import { ScrollView, StyleSheet, Text, TextInput, View } from 'react-native'
import { useRouter } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { enqueue } from '../kernel/sync/outbox'
import { runSync } from '../kernel/sync/engine'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import {
  addLocalReceiptLine,
  discardLocalReceipt,
  findDocByCode,
  findProductByCode,
  getOpenReceipt,
  removeLocalReceiptLine,
  startLocalReceipt,
} from '../features/receive/localLookup'
import {
  addSerial,
  buildLine,
  buildReceiptBody,
  canAddLine,
  type LineDraft,
  newLineDraft,
  removeSerial,
  requiresLot,
  requiresSerials,
} from '../features/receive/receiveLogic'

/** Pantalla 3 (docs/mobile/app-almacen-plan.md §2): escanea una orden/aviso o recibo ciego → escanea producto →
 *  cantidad/lote/series → siguiente; Confirmar cierra el recibo y lo manda a la cola de salida. */
export default function ReceiveScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null
  const [tick, setTick] = useState(0)
  const [docError, setDocError] = useState<string | null>(null)
  const [productError, setProductError] = useState<string | null>(null)
  const [draft, setDraft] = useState<LineDraft | null>(null)

  // tick fuerza releer la base local tras cada mutación (start/add/remove/confirm); getOpenReceipt() no usa tick.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const openReceipt = useMemo(() => getOpenReceipt(), [tick])
  const refresh = () => setTick((n) => n + 1)

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function startBlind() {
    startLocalReceipt(warehousePublicId!, null)
    setDocError(null)
    refresh()
  }

  function scanDoc(code: string) {
    const doc = findDocByCode(warehousePublicId!, code)
    if (!doc) {
      setDocError(t('receive.docNotFound'))
      vibrateError()
      return
    }
    startLocalReceipt(warehousePublicId!, doc)
    setDocError(null)
    vibrateOk()
    refresh()
  }

  function scanProduct(code: string) {
    const product = findProductByCode(code)
    if (!product) {
      setProductError(t('receive.productNotFound'))
      vibrateError()
      return
    }
    setProductError(null)
    setDraft(newLineDraft(product))
    vibrateOk()
  }

  function addCurrentLine() {
    if (!draft || !openReceipt || !canAddLine(draft)) return
    addLocalReceiptLine(openReceipt.id, buildLine(draft))
    setDraft(null)
    vibrateOk()
    refresh()
  }

  function confirmReceipt() {
    if (!openReceipt || openReceipt.lines.length === 0) return
    const body = buildReceiptBody(openReceipt.warehousePublicId, openReceipt.doc, openReceipt.lines)
    enqueue({ kind: 'receipt', body })
    discardLocalReceipt()
    void runSync()
    refresh()
    router.replace('/home')
  }

  // Paso 1: sin recibo abierto todavía.
  if (!openReceipt) {
    return (
      <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>{t('receive.title')}</Text>
        <ScanField label={t('receive.scanDocLabel')} help={t('receive.scanDocHelp')} error={docError} onSubmit={scanDoc} />
        <BigButton label={t('receive.startBlind')} variant="secondary" onPress={startBlind} />
        <Text style={styles.help}>{t('receive.startBlindHelp')}</Text>
      </ScrollView>
    )
  }

  // Paso 3: capturando cantidad/lote/series de un producto ya escaneado.
  if (draft) {
    return (
      <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>{draft.productName}</Text>
        <Text style={styles.help}>{draft.sku}</Text>

        {requiresSerials(draft) ? (
          <View style={styles.field}>
            <ScanField
              label={t('receive.serialsLabel')}
              onSubmit={(code) => {
                setDraft((d) => (d ? addSerial(d, code) : d))
                          }}
            />
            <LineList
              items={draft.serials.map((s) => ({ id: s, title: s }))}
              onRemove={(id) => setDraft((d) => (d ? removeSerial(d, String(id)) : d))}
              removeLabel={t('common.remove')}
            />
          </View>
        ) : (
          <View style={styles.field}>
            <Text style={styles.label}>{t('receive.qtyLabel')}</Text>
            <TextInput
              value={draft.qtyText}
              onChangeText={(v) => setDraft((d) => (d ? { ...d, qtyText: v } : d))}
              keyboardType="decimal-pad"
              style={styles.input}
              accessibilityLabel={t('receive.qtyLabel')}
            />
          </View>
        )}

        {requiresLot(draft) ? (
          <>
            <View style={styles.field}>
              <Text style={styles.label}>{t('receive.lotLabel')}</Text>
              <TextInput
                value={draft.lot}
                onChangeText={(v) => setDraft((d) => (d ? { ...d, lot: v } : d))}
                style={styles.input}
                accessibilityLabel={t('receive.lotLabel')}
              />
            </View>
            <View style={styles.field}>
              <Text style={styles.label}>{t('receive.expiryLabel')}</Text>
              <TextInput
                value={draft.expiry}
                onChangeText={(v) => setDraft((d) => (d ? { ...d, expiry: v } : d))}
                placeholder="AAAA-MM-DD"
                placeholderTextColor={colors.muted}
                style={styles.input}
                accessibilityLabel={t('receive.expiryLabel')}
              />
            </View>
          </>
        ) : null}

        <View style={styles.row}>
          <BigButton label={t('common.cancel')} variant="secondary" onPress={() => setDraft(null)} />
          <BigButton label={t('receive.addLine')} onPress={addCurrentLine} disabled={!canAddLine(draft)} />
        </View>
      </ScrollView>
    )
  }

  // Paso 2: recibo abierto, escaneando productos y viendo lo ya capturado.
  return (
    <View style={styles.fill}>
      <Text style={styles.title}>
        {openReceipt.doc
          ? t(openReceipt.doc.kind === 'asn' ? 'receive.docLabelAsn' : 'receive.docLabelPo', {
              number: openReceipt.doc.label,
              reference: openReceipt.doc.label,
            })
          : t('receive.startBlind')}
      </Text>
      <ScanField label={t('receive.scanProductLabel')} help={t('receive.scanProductHelp')} error={productError} onSubmit={scanProduct} />
      <Text style={styles.label}>{t('receive.linesTitle')}</Text>
      <LineList
        items={openReceipt.lines.map((l, i) => ({
          id: openReceipt.lineRows[i].id,
          title: t('receive.lineQty', { qty: l.receivedQty, sku: l.sku }),
          subtitle: l.lotNumber ?? undefined,
        }))}
        onRemove={(id) => {
          removeLocalReceiptLine(Number(id))
          refresh()
        }}
        removeLabel={t('common.remove')}
        emptyLabel={t('receive.linesTitle')}
      />
      <BigButton label={t('receive.confirmReceipt')} onPress={confirmReceipt} disabled={openReceipt.lines.length === 0} />
      <Text style={styles.help}>{t('receive.confirmHelp')}</Text>
    </View>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: 20, fontWeight: '700' },
  label: { color: colors.text, fontSize: 16, fontWeight: '600' },
  help: { color: colors.muted, fontSize: 13 },
  error: { color: colors.error, fontSize: 15 },
  field: { gap: spacing.xs },
  row: { flexDirection: 'row', gap: spacing.md },
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
