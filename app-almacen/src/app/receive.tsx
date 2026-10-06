import { useEffect, useMemo, useState } from 'react'
import { Alert, Pressable, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { enqueue } from '../kernel/sync/outbox'
import { runSync } from '../kernel/sync/engine'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { useFormat } from '../kernel/format/useFormat'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import {
  addLocalReceiptLine,
  countLocalBins,
  discardLocalReceipt,
  findDocByCode,
  findLocalBinsByCode,
  findProductByCode,
  getDocLines,
  getOpenReceipt,
  removeLocalReceiptLine,
  startLocalReceipt,
} from '../features/receive/localLookup'
import { fetchTargetSuggestion } from '../features/receive/receiveApi'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import {
  addSerial,
  buildLine,
  buildReceiptBody,
  canAddLine,
  draftExpiry,
  draftQuantity,
  findTargetConflict,
  type LineDraft,
  linesMissingTarget,
  newLineDraft,
  parseReceivingMode,
  type ReceivingMode,
  removeSerial,
  requiresLot,
  requiresSerials,
  validateTargetBin,
} from '../features/receive/receiveLogic'

/** Pantalla 3 (docs/mobile/app-almacen-plan.md §2): escanea una orden/aviso o recibo ciego → escanea producto →
 *  cantidad/lote/series → siguiente; Confirmar cierra el recibo y lo manda a la cola de salida.
 *  Lote 16: si el recibo se abrió en modo "Directo a posición" (el del almacén del aparato al empezarlo), tras la cantidad
 *  viene el paso "Escanea la posición destino" (validada contra las posiciones locales, con la pista "Sugerida: …" si hay
 *  señal); la línea se agrega al escanear una posición válida y se muestra con "→ {posición}". Con acomodo, igual que antes. */
export default function ReceiveScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const { device } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null
  const deviceMode = parseReceivingMode(device?.defaultWarehouseReceivingMode)
  // Modo de este recibo: por defecto el del almacén; se puede cambiar antes de abrirlo (solo vale para ese recibo).
  const [modeChoice, setModeChoice] = useState<ReceivingMode | null>(null)
  // Sin elección y sin modo conocido del almacén se manda null, como antes (el servidor usa el del almacén).
  const sentMode: ReceivingMode | null = modeChoice ?? deviceMode
  const startMode: ReceivingMode = sentMode ?? 'PUTAWAY'
  const [tick, setTick] = useState(0)
  const [docError, setDocError] = useState<string | null>(null)
  const [productError, setProductError] = useState<string | null>(null)
  const [draft, setDraft] = useState<LineDraft | null>(null)
  // Lote 16: paso de posición destino del borrador (solo recibo directo), su error y la pista del servidor.
  const [askTarget, setAskTarget] = useState(false)
  const [targetError, setTargetError] = useState<string | null>(null)
  const [suggestion, setSuggestion] = useState<string | null>(null)

  // tick fuerza releer la base local tras cada mutación (start/add/remove/confirm); getOpenReceipt() no usa tick.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const openReceipt = useMemo(() => getOpenReceipt(), [tick])
  const refresh = () => setTick((n) => n + 1)
  const direct = openReceipt?.receivingMode === 'DIRECT'

  // Pista "Sugerida: {bin}" al entrar al paso de destino (una sola, take=1). Sin señal no aparece; no se llena nada solo.
  const suggestProduct = draft && askTarget ? draft.productPublicId : null
  const suggestQty = draft && askTarget ? draftQuantity(draft) : 0
  useEffect(() => {
    if (!suggestProduct || !warehousePublicId) return undefined
    let alive = true
    void fetchTargetSuggestion(suggestProduct, warehousePublicId, suggestQty).then((bin) => {
      if (alive) setSuggestion(bin)
    })
    return () => {
      alive = false
    }
  }, [suggestProduct, suggestQty, warehousePublicId])

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function startBlind() {
    startLocalReceipt(warehousePublicId!, null, sentMode)
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
    startLocalReceipt(warehousePublicId!, doc, sentMode)
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

  function closeDraft() {
    setDraft(null)
    setAskTarget(false)
    setTargetError(null)
    setSuggestion(null)
  }

  function addCurrentLine() {
    if (!draft || !openReceipt || !canAddLine(draft)) return
    if (direct) {
      // Recibo directo: la cantidad está lista; falta dónde queda la mercancía.
      setTargetError(null)
      setSuggestion(null)
      setAskTarget(true)
      return
    }
    addLocalReceiptLine(openReceipt.id, buildLine(draft))
    closeDraft()
    vibrateOk()
    refresh()
  }

  /** Lote 16: la posición destino escaneada. Valida sin señal contra las posiciones locales y, en recibos con aviso u
   *  orden de compra, que el mismo producto no quede con dos destinos (H11); si todo cuadra agrega la línea. */
  function scanTarget(code: string) {
    if (!draft || !openReceipt) return
    const fail = (message: string) => {
      setTargetError(message)
      vibrateError()
    }
    if (countLocalBins(openReceipt.warehousePublicId) === 0) return fail(t('receive.targetNoBins'))
    const check = validateTargetBin(code, findLocalBinsByCode(openReceipt.warehousePublicId, code))
    if (!check.ok) {
      const key =
        check.reason === 'notStorage' ? 'receive.targetNotStorage' : check.reason === 'inactive' ? 'receive.targetInactive' : 'receive.targetNotFound'
      return fail(t(key))
    }
    const withTarget = { ...draft, targetBinCode: check.code }
    if (!canAddLine(withTarget, true)) return
    const line = buildLine(withTarget)
    if (openReceipt.doc) {
      const lines = [...openReceipt.lines, line]
      const conflict = findTargetConflict(getDocLines(openReceipt.doc), lines)
      if (conflict && conflict.index === lines.length - 1) return fail(t('receive.targetConflict', { sku: conflict.sku, bin: conflict.bin }))
    }
    addLocalReceiptLine(openReceipt.id, line)
    closeDraft()
    vibrateOk()
    refresh()
  }

  function cancelReceipt() {
    Alert.alert(t('receive.cancelConfirmTitle'), t('receive.cancelConfirmBody'), [
      { text: t('common.no'), style: 'cancel' },
      {
        text: t('receive.cancelReceipt'),
        style: 'destructive',
        onPress: () => {
          discardLocalReceipt()
          closeDraft()
          setProductError(null)
          refresh()
        },
      },
    ])
  }

  function confirmReceipt() {
    if (!openReceipt || openReceipt.lines.length === 0) return
    if (direct && openReceipt.doc) {
      // H11: el servidor rechazaría el recibo completo y un envío rechazado en la cola ya no se edita; se avisa aquí.
      const conflict = findTargetConflict(getDocLines(openReceipt.doc), openReceipt.lines)
      if (conflict) {
        vibrateError()
        Alert.alert(t('receive.targetConflictTitle'), t('receive.targetConflict', { sku: conflict.sku, bin: conflict.bin }))
        return
      }
    }
    const body = buildReceiptBody(openReceipt.warehousePublicId, openReceipt.doc, openReceipt.lines, openReceipt.receivingMode)
    enqueue({ kind: 'receipt', body })
    discardLocalReceipt()
    void runSync()
    refresh()
    router.replace('/home')
  }

  // Paso 1: sin recibo abierto todavía. Nada que perder aquí, así que "Volver" sale directo a Inicio (a diferencia
  // de los pasos con captura en curso, donde la salida es a propósito por Confirmar o Cancelar). 2026-10-01 (Luis): va
  // abajo del todo, debajo del texto de ayuda de "Recibo ciego" (con `marginTop: auto` queda pegado al borde inferior).
  if (!openReceipt) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('receive.title')}</Text>
        <Text style={styles.label}>{t('receive.modeLabel')}</Text>
        <View style={styles.row}>
          {(['PUTAWAY', 'DIRECT'] as const).map((m) => (
            <Pressable
              key={m}
              accessibilityRole="radio"
              accessibilityState={{ selected: startMode === m }}
              testID={`receive-mode-${m}`}
              onPress={() => setModeChoice(m)}
              style={[styles.modeBtn, startMode === m && styles.modeBtnOn]}
            >
              <Text style={styles.modeText}>{t(m === 'DIRECT' ? 'receive.modeDirect' : 'receive.modePutaway')}</Text>
            </Pressable>
          ))}
        </View>
        <Text style={styles.help}>{t(startMode === 'DIRECT' ? 'receive.modeDirectHelp' : 'receive.modePutawayHelp')}</Text>
        <ScanField label={t('receive.scanDocLabel')} help={t('receive.scanDocHelp')} error={docError} onSubmit={scanDoc} />
        <BigButton label={t('receive.startBlind')} variant="secondary" onPress={startBlind} />
        <Text style={styles.help}>{t('receive.startBlindHelp')}</Text>
        <View style={styles.bottom}>
          <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
        </View>
      </KeyboardScreen>
    )
  }

  // Paso 3b (Lote 16, solo recibo directo): la posición destino de la línea. Un solo campo de captura en pantalla (el
  // lector escribe en todos los ScanField montados), por eso es un paso aparte y no un campo más del paso 3.
  if (draft && askTarget) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{draft.productName}</Text>
        <Text style={styles.help}>{t('receive.lineQty', { qty: draftQuantity(draft), sku: draft.sku })}</Text>
        {suggestion ? <Text style={styles.hint}>{t('receive.targetHint', { bin: suggestion })}</Text> : null}
        <ScanField label={t('receive.scanTargetLabel')} error={targetError} onSubmit={scanTarget} suggestedValue={suggestion} />
        <View style={styles.row}>
          <BigButton
            label={t('common.back')}
            variant="secondary"
            onPress={() => {
              setAskTarget(false)
              setTargetError(null)
            }}
          />
          <BigButton label={t('common.cancel')} variant="danger" onPress={closeDraft} />
        </View>
      </KeyboardScreen>
    )
  }

  // Paso 3: capturando cantidad/lote/series de un producto ya escaneado.
  if (draft) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
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
            <KeyboardInput
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
              <KeyboardInput
                value={draft.lot}
                onChangeText={(v) => setDraft((d) => (d ? { ...d, lot: v } : d))}
                style={styles.input}
                accessibilityLabel={t('receive.lotLabel')}
              />
            </View>
            <View style={styles.field}>
              <Text style={styles.label}>{t('receive.expiryLabel')}</Text>
              <KeyboardInput
                value={draft.expiry}
                onChangeText={(v) => setDraft((d) => (d ? { ...d, expiry: v } : d))}
                placeholder={f.datePlaceholder()}
                placeholderTextColor={colors.muted}
                style={styles.input}
                accessibilityLabel={t('receive.expiryLabel')}
              />
              {/* región y formatos: la fecha se escribe en el orden de la compañía (o en ISO) */}
              {draftExpiry(draft, f.settings) === null ? (
                <Text style={styles.error}>{t('receive.expiryInvalid', { format: f.datePlaceholder() })}</Text>
              ) : null}
            </View>
          </>
        ) : null}

        <View style={styles.row}>
          <BigButton label={t('common.cancel')} variant="secondary" onPress={closeDraft} />
          <BigButton label={t(direct ? 'common.next' : 'receive.addLine')} onPress={addCurrentLine} disabled={!canAddLine(draft)} />
        </View>
      </KeyboardScreen>
    )
  }

  // Paso 2: recibo abierto, escaneando productos y viendo lo ya capturado. ScrollView (no View, como los otros dos
  // pasos): el ScanField se reenfoca tras cada línea agregada y, con el teclado abierto, tapaba Confirmar/Cancelar.
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
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
          subtitle:
            [l.lotNumber, l.targetBinCode ? t('receive.lineTarget', { bin: l.targetBinCode }) : null].filter(Boolean).join(' · ') ||
            undefined,
        }))}
        onRemove={(id) => {
          removeLocalReceiptLine(Number(id))
          refresh()
        }}
        removeLabel={t('common.remove')}
        emptyLabel={t('receive.linesTitle')}
      />
      <BigButton
        label={t('receive.confirmReceipt')}
        onPress={confirmReceipt}
        disabled={openReceipt.lines.length === 0 || (direct && linesMissingTarget(openReceipt.lines) > 0)}
      />
      <Text style={styles.help}>{t(direct ? 'receive.confirmHelpDirect' : 'receive.confirmHelp')}</Text>
      <BigButton label={t('receive.cancelReceipt')} variant="danger" onPress={cancelReceipt} />
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  hint: { color: colors.warn, fontSize: 18, fontWeight: '700' },
  error: { color: colors.error, fontSize: fontSize.message },
  field: { gap: spacing.xs },
  modeBtn: {
    flex: 1,
    minHeight: 56,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.sm,
    borderRadius: 12,
    borderWidth: 2,
    borderColor: colors.line,
    backgroundColor: colors.panelAlt,
  },
  modeBtnOn: { borderColor: colors.brand },
  modeText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600', textAlign: 'center' },
  // Pega el botón al borde inferior cuando el contenido es corto (el contenedor del ScrollView crece: flexGrow 1).
  bottom: { marginTop: 'auto' },
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
