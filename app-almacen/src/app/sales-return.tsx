import { useMemo, useRef, useState } from 'react'
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { useFormat } from '../kernel/format/useFormat'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { useT } from '../kernel/i18n/useT'
import { downloadSalesReturnsIfAllowed } from '../kernel/sync/download'
import { flushNow } from '../kernel/sync/engine'
import { discardRow } from '../kernel/sync/outbox'
import { BigButton } from '../kernel/ui/BigButton'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { ScanField } from '../kernel/ui/ScanField'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { findSalesReturnByNumber, getSalesReturnLines, listSalesReturns, queueReturnReceive, type LocalReturn } from '../features/salesReturn/localReturns'
import { checkReceive, initialEntries, totalReceived, type LineEntry, type ReceiveBlock } from '../features/salesReturn/returnReceiveLogic'

type Step = 'list' | 'receive' | 'sending'

/** Recibir una devolución de venta (Lote 83, P6): la lista de devoluciones autorizadas del almacén, el escaneo del número (DV-…) y, por línea, lo que llegó BUENO y lo que llegó DAÑADO (con qué
 *  le pasó y el lote si lleva). Va por la cola de salida (sirve sin señal) y el servidor lo valida al sincronizar; si lo rechaza, se muestra su mensaje exacto. */
export default function SalesReturnScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const warehousePublicId = useActiveWarehouse().publicId
  const [step, setStep] = useState<Step>('list')
  const [tick, setTick] = useState(0)
  const [ret, setRet] = useState<LocalReturn | null>(null)
  const [entries, setEntries] = useState<Record<number, LineEntry>>({})
  const [closeRest, setCloseRest] = useState(false)
  const [scanError, setScanError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const lock = useRef(false)

  // tick fuerza releer la copia local tras volver de enviar.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const returns = useMemo(() => (warehousePublicId ? listSalesReturns(warehousePublicId) : []), [warehousePublicId, tick])
  const lines = useMemo(() => (ret ? getSalesReturnLines(ret.id) : []), [ret])

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function open(r: LocalReturn) {
    setRet(r)
    setEntries(initialEntries(getSalesReturnLines(r.id)))
    setCloseRest(false)
    setProblem(null)
    setScanError(null)
    setNotice(null)
    setStep('receive')
    vibrateOk()
  }

  function scanReturn(code: string) {
    const found = findSalesReturnByNumber(warehousePublicId!, code)
    if (!found || found.pending <= 0) {
      setScanError(t('salesReturn.returnNotFound'))
      vibrateError()
      return
    }
    open(found)
  }

  function setEntry(id: number, patch: Partial<LineEntry>) {
    setProblem(null)
    setEntries((x) => ({ ...x, [id]: { ...x[id], ...patch } }))
  }

  function blockText(b: ReceiveBlock): string {
    return t(`salesReturn.err${b.kind[0].toUpperCase()}${b.kind.slice(1)}`, { sku: b.sku ?? '', pending: f.qty(b.pending ?? 0) })
  }

  async function send() {
    if (!ret || lock.current) return
    const block = checkReceive(lines, entries)
    if (block) {
      setProblem(blockText(block))
      vibrateError()
      return
    }
    lock.current = true
    setStep('sending')
    let id = 0
    try {
      id = queueReturnReceive(ret, lines, entries, closeRest)
    } catch {
      lock.current = false
      setStep('receive')
      setProblem(t('errors.generic'))
      vibrateError()
      return
    }
    const result = await flushNow(id)
    lock.current = false
    if (result.status === 'rejected') {
      // el servidor no la aceptó: la fila no queda en Sincronización; se muestra el mensaje exacto y se baja la devolución como está
      discardRow(id)
      void downloadSalesReturnsIfAllowed(warehousePublicId!).catch(() => undefined)
      setRet(null)
      setStep('list')
      setScanError(result.error || t('errors.generic'))
      setNotice(null)
      setTick((x) => x + 1)
      vibrateError()
      return
    }
    setNotice(result.status === 'sent' ? t('salesReturn.sent') : t('salesReturn.queued'))
    setScanError(null)
    setRet(null)
    setStep('list')
    setTick((x) => x + 1)
    vibrateOk()
  }

  if (step === 'sending') {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('salesReturn.title')}</Text>
        <ActivityIndicator color={colors.brand} />
        <Text style={styles.help}>{t('salesReturn.sending')}</Text>
      </KeyboardScreen>
    )
  }

  if (step === 'list' || !ret) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('salesReturn.title')}</Text>
        <ScanField label={t('salesReturn.scanReturn')} help={t('salesReturn.scanReturnHelp')} error={scanError} notice={notice} onSubmit={scanReturn} testID="return-scan" />
        {returns.length === 0 ? <Text style={styles.help}>{t('salesReturn.noReturns')}</Text> : null}
        {returns.map((r) => (
          <Pressable
            key={r.id}
            accessibilityRole="button"
            accessibilityLabel={t('salesReturn.openReturn', { number: r.number })}
            onPress={() => open(r)}
            style={({ pressed }) => [styles.card, pressed && styles.cardPressed]}
            testID={`return-${r.number}`}
          >
            <Text style={styles.cardTitle}>{r.number}</Text>
            <Text style={styles.help}>{[r.clientName, t('salesReturn.forOrder', { order: r.orderNumber })].filter(Boolean).join(' · ')}</Text>
            <Text style={styles.help}>{t('salesReturn.toReceive', { qty: f.qty(r.pending) })}</Text>
          </Pressable>
        ))}
        <View style={styles.bottom}>
          <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
        </View>
      </KeyboardScreen>
    )
  }

  const total = totalReceived(lines, entries)
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{ret.number}</Text>
      <Text style={styles.help}>{[ret.clientName, t('salesReturn.forOrder', { order: ret.orderNumber })].filter(Boolean).join(' · ')}</Text>
      {ret.note ? <Text style={styles.help}>{ret.note}</Text> : null}

      {lines.map((l) => {
        const e = entries[l.id]
        if (!e) return null
        const hasDamage = (Number(e.damaged.replace(',', '.')) || 0) > 0
        return (
          <View key={l.id} style={styles.current} testID={`return-line-${l.sku}`}>
            <Text style={styles.cardTitle}>
              {l.sku} · {l.productName}
            </Text>
            <Text style={styles.help}>{t('salesReturn.pendingOf', { qty: f.qty(l.pending) })}</Text>
            <View style={styles.field}>
              <Text style={styles.label}>{t('salesReturn.good')}</Text>
              <KeyboardInput value={e.good} onChangeText={(v) => setEntry(l.id, { good: v })} keyboardType="decimal-pad" style={styles.input} accessibilityLabel={`${t('salesReturn.good')} ${l.sku}`} testID={`return-good-${l.sku}`} />
            </View>
            <View style={styles.field}>
              <Text style={styles.label}>{t('salesReturn.damaged')}</Text>
              <KeyboardInput value={e.damaged} onChangeText={(v) => setEntry(l.id, { damaged: v })} keyboardType="decimal-pad" style={styles.input} accessibilityLabel={`${t('salesReturn.damaged')} ${l.sku}`} testID={`return-damaged-${l.sku}`} />
            </View>
            {hasDamage ? (
              <>
                <View style={styles.field}>
                  <Text style={styles.label}>{t('salesReturn.damageNote')}</Text>
                  <KeyboardInput value={e.note} onChangeText={(v) => setEntry(l.id, { note: v })} style={styles.input} maxLength={250} accessibilityLabel={`${t('salesReturn.damageNote')} ${l.sku}`} testID={`return-note-${l.sku}`} />
                </View>
                <Pressable accessibilityRole="checkbox" accessibilityState={{ checked: e.discard }} onPress={() => setEntry(l.id, { discard: !e.discard })} style={styles.check} testID={`return-discard-${l.sku}`}>
                  <Text style={styles.label}>
                    {e.discard ? '☑' : '☐'} {t('salesReturn.discard')}
                  </Text>
                </Pressable>
              </>
            ) : null}
            {l.trackingTypeCode === 'LOT' ? (
              <View style={styles.field}>
                <Text style={styles.label}>{t('salesReturn.lot')}</Text>
                <KeyboardInput value={e.lot} onChangeText={(v) => setEntry(l.id, { lot: v })} style={styles.input} autoCapitalize="characters" accessibilityLabel={`${t('salesReturn.lot')} ${l.sku}`} testID={`return-lot-${l.sku}`} />
              </View>
            ) : null}
          </View>
        )
      })}

      <Pressable accessibilityRole="checkbox" accessibilityState={{ checked: closeRest }} onPress={() => setCloseRest((x) => !x)} style={styles.check} testID="return-close-rest">
        <Text style={styles.label}>
          {closeRest ? '☑' : '☐'} {t('salesReturn.closeRest')}
        </Text>
      </Pressable>
      <Text style={styles.label}>{t('salesReturn.summary', { good: f.qty(total.good), damaged: f.qty(total.damaged) })}</Text>
      {problem ? <Text style={styles.error}>{problem}</Text> : null}

      <BigButton label={t('salesReturn.receive')} onPress={() => void send()} testID="return-receive" />
      <BigButton
        label={t('salesReturn.cancel')}
        variant="danger"
        onPress={() => {
          setRet(null)
          setProblem(null)
          setStep('list')
        }}
      />
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  field: { gap: spacing.xs },
  check: { minHeight: 48, justifyContent: 'center' },
  bottom: { marginTop: 'auto' },
  card: { gap: 2, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.panelAlt },
  cardPressed: { backgroundColor: colors.panel },
  cardTitle: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  current: { gap: spacing.sm, padding: spacing.md, borderRadius: 12, borderWidth: 2, borderColor: colors.brand, backgroundColor: colors.panelAlt },
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
