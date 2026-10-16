import { useMemo, useRef, useState } from 'react'
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { useFormat } from '../kernel/format/useFormat'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { resolveBinLocalFirst } from '../kernel/warehouse/binLookup'
import { useT } from '../kernel/i18n/useT'
import { downloadSalesOrdersIfAllowed } from '../kernel/sync/download'
import { flushNow } from '../kernel/sync/engine'
import { discardRow, outboxResult } from '../kernel/sync/outbox'
import { BigButton } from '../kernel/ui/BigButton'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { ScanField } from '../kernel/ui/ScanField'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { findSalesOrderByNumber, getSalesOrderLines, listSalesOrders, queueSalesDispatch, type LocalSalesOrder } from '../features/salesDispatch/localSales'
import {
  buildTasks,
  checkPickQty,
  kitSummaries,
  dispatchBatchNumber,
  nextTask,
  parseQty,
  totalPicked,
  type SalesPick,
  type SalesTask,
} from '../features/salesDispatch/salesDispatchLogic'

type Step = 'list' | 'pick' | 'finish' | 'pieces' | 'sending'

/** Despachar contra una orden de venta (Lote 65, P4 S2): la lista de órdenes con algo reservado, el escaneo del número de la orden y el despacho GUIADO por lo que el
 *  sistema reservó (posición → cantidad). Al terminar: Retiro, Entrega (con bultos; solo si la orden tiene consignatario) o Dejar para después. El despacho va por la cola de
 *  salida (sirve sin señal) y el servidor lo valida al sincronizar: la existencia de la posición y que no se despache más de lo reservado. */
export default function SalesDispatchScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const warehousePublicId = useActiveWarehouse().publicId
  const [step, setStep] = useState<Step>('list')
  const [tick, setTick] = useState(0)
  const [order, setOrder] = useState<LocalSalesOrder | null>(null)
  const [picks, setPicks] = useState<SalesPick[]>([])
  const [skipped, setSkipped] = useState<Set<string>>(new Set())
  const [qtyText, setQtyText] = useState('')
  const [pieces, setPieces] = useState('1')
  const [scanError, setScanError] = useState<string | null>(null)
  const [binError, setBinError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const lock = useRef(false)

  // tick fuerza releer la copia local tras volver de enviar.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const orders = useMemo(() => (warehousePublicId ? listSalesOrders(warehousePublicId) : []), [warehousePublicId, tick])
  const lines = useMemo(() => (order ? getSalesOrderLines(order.id) : []), [order])
  const tasks = useMemo(() => buildTasks(lines), [lines])
  const current = order && step === 'pick' ? nextTask(tasks, picks, skipped) : null
  const doneAll = step === 'pick' && order !== null && current === null

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function open(o: LocalSalesOrder) {
    setOrder(o)
    setPicks([])
    setSkipped(new Set())
    setBinError(null)
    setScanError(null)
    setNotice(null)
    setStep('pick')
    const first = nextTask(buildTasks(getSalesOrderLines(o.id)), [], new Set())
    setQtyText(first ? String(first.qty) : '')
    vibrateOk()
  }

  function scanOrder(code: string) {
    const found = findSalesOrderByNumber(warehousePublicId!, code)
    if (!found || found.reserved <= 0) {
      setScanError(t('salesDispatch.orderNotFound'))
      vibrateError()
      return
    }
    open(found)
  }

  function advance(nextPicks: SalesPick[], nextSkipped: Set<string>) {
    setPicks(nextPicks)
    setSkipped(nextSkipped)
    const n = nextTask(tasks, nextPicks, nextSkipped)
    setQtyText(n ? String(n.qty) : '')
    setBinError(null)
  }

  /** Lee la posición de la tarea actual: la reservada o cualquier otra (la reserva se mueve ahí si tiene existencia; el servidor lo valida). */
  async function scanBin(task: SalesTask, code: string) {
    const block = checkPickQty(task, qtyText)
    if (block) {
      setBinError(t(block === 'qtyInvalid' ? 'salesDispatch.qtyInvalid' : 'salesDispatch.qtyTooMuch', { qty: f.qty(task.qty) }))
      vibrateError()
      return
    }
    const bin = await resolveBinLocalFirst(warehousePublicId!, code).catch(() => null)
    if (!bin) {
      setBinError(t('salesDispatch.binNotFound'))
      vibrateError()
      return
    }
    const qty = parseQty(qtyText) ?? 0
    advance([...picks, { taskKey: task.key, binId: bin.id, binCode: bin.code, qty }], skipped)
    if (bin.id !== task.binId) setNotice(t('salesDispatch.binMoves', { bin: bin.code }))
    else setNotice(null)
    vibrateOk()
  }

  function skip(task: SalesTask) {
    advance(picks, new Set([...skipped, task.key]))
  }

  function undo(key: string) {
    const nextPicks = picks.filter((p) => p.taskKey !== key)
    const nextSkipped = new Set([...skipped].filter((k) => k !== key))
    advance(nextPicks, nextSkipped)
  }

  async function send(withDelivery: boolean) {
    if (!order || lock.current) return
    const incomplete = kitSummaries(tasks, picks).find((k) => k.hasPicks && k.kits === 0)
    if (incomplete) {
      setScanError(t('salesDispatch.kitIncomplete', { kit: incomplete.kitSku }))
      vibrateError()
      return
    }
    const n = Math.max(1, Math.round(Number(pieces) || 1))
    lock.current = true
    setStep('sending')
    let id = 0
    try {
      id = queueSalesDispatch(order, tasks, picks, withDelivery ? { packages: n } : null)
    } catch {
      lock.current = false
      setStep('finish')
      setScanError(t('errors.generic'))
      vibrateError()
      return
    }
    const result = await flushNow(id)
    lock.current = false
    if (result.status === 'rejected') {
      // el servidor ya deshizo los saldos (runOutbox); la fila no queda en Sincronización: se muestra el mensaje exacto y se baja la orden como está
      discardRow(id)
      void downloadSalesOrdersIfAllowed(warehousePublicId!).catch(() => undefined)
      setOrder(null)
      setPicks([])
      setStep('list')
      setScanError(result.error || t('errors.generic'))
      setNotice(null)
      setTick((x) => x + 1)
      vibrateError()
      return
    }
    const number = result.status === 'sent' ? dispatchBatchNumber(outboxResult(id)) : null
    setNotice(result.status === 'sent' ? (number ? t('salesDispatch.sent', { number }) : t('salesDispatch.sentNoNumber')) : t('salesDispatch.queued'))
    setScanError(null)
    setOrder(null)
    setPicks([])
    setStep('list')
    setTick((x) => x + 1)
    vibrateOk()
  }

  if (step === 'sending') {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('salesDispatch.title')}</Text>
        <ActivityIndicator color={colors.brand} />
        <Text style={styles.help}>{t('salesDispatch.sending')}</Text>
      </KeyboardScreen>
    )
  }

  if (step === 'list' || !order) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('salesDispatch.title')}</Text>
        <ScanField label={t('salesDispatch.scanOrder')} help={t('salesDispatch.scanOrderHelp')} error={scanError} notice={notice} onSubmit={scanOrder} testID="sales-scan-order" />
        {orders.length === 0 ? <Text style={styles.help}>{t('salesDispatch.noOrders')}</Text> : null}
        {orders.map((o) => (
          <Pressable
            key={o.id}
            accessibilityRole="button"
            accessibilityLabel={t('salesDispatch.openOrder', { number: o.number })}
            onPress={() => open(o)}
            style={({ pressed }) => [styles.card, pressed && styles.cardPressed]}
            testID={`sales-order-${o.number}`}
          >
            <Text style={styles.cardTitle}>{o.number}</Text>
            <Text style={styles.help}>{[o.clientName, o.consigneeName].filter(Boolean).join(' · ')}</Text>
            <Text style={styles.help}>
              {t('salesDispatch.toDispatch', { qty: f.qty(o.reserved) })}
              {o.requiredDate ? ' · ' + t('salesDispatch.required', { date: f.date(o.requiredDate) }) : ''}
            </Text>
          </Pressable>
        ))}
        <View style={styles.bottom}>
          <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
        </View>
      </KeyboardScreen>
    )
  }

  if (step === 'finish' || step === 'pieces') {
    const total = totalPicked(picks)
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('salesDispatch.finishTitle', { number: order.number })}</Text>
        <Text style={styles.label}>{t('salesDispatch.finishSummary', { lines: picks.length, qty: f.qty(total) })}</Text>
        {kitSummaries(tasks, picks).filter((k) => k.hasPicks).map((k) => (
          <Text key={k.lineId} style={styles.help}>
            {t('salesDispatch.kitSummary', { kit: k.kitSku, kits: k.kits })}
          </Text>
        ))}
        {scanError ? <Text style={styles.error}>{scanError}</Text> : null}
        {step === 'finish' ? (
          <>
            <Text style={styles.help}>{t('salesDispatch.nextHelp')}</Text>
            <BigButton label={t('salesDispatch.pickup')} onPress={() => void send(false)} testID="sales-pickup" />
            {order.consigneeName ? <BigButton label={t('salesDispatch.deliver')} variant="secondary" onPress={() => setStep('pieces')} testID="sales-deliver" /> : null}
            <BigButton label={t('salesDispatch.later')} variant="secondary" onPress={() => void send(false)} testID="sales-later" />
            <Text style={styles.help}>{t('salesDispatch.laterHelp')}</Text>
            <BigButton label={t('common.back')} variant="secondary" onPress={() => setStep('pick')} />
          </>
        ) : (
          <>
            <Text style={styles.help}>{t('salesDispatch.deliverTo', { name: order.consigneeName ?? '' })}</Text>
            <View style={styles.field}>
              <Text style={styles.label}>{t('dispatch.piecesLabel')}</Text>
              <KeyboardInput value={pieces} onChangeText={setPieces} keyboardType="number-pad" style={styles.input} accessibilityLabel={t('dispatch.piecesLabel')} testID="sales-pieces" />
            </View>
            <BigButton label={t('salesDispatch.deliverConfirm')} onPress={() => void send(true)} testID="sales-deliver-confirm" />
            <BigButton label={t('common.back')} variant="secondary" onPress={() => setStep('finish')} />
          </>
        )}
      </KeyboardScreen>
    )
  }

  // step === 'pick'
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{order.number}</Text>
      <Text style={styles.help}>{[order.clientName, order.consigneeName].filter(Boolean).join(' · ')}</Text>
      {notice ? <Text style={styles.okText}>{notice}</Text> : null}

      {current ? (
        <View style={styles.current} testID="sales-current">
          <Text style={styles.cardTitle}>{t('salesDispatch.goTo', { bin: current.binCode })}</Text>
          <Text style={styles.label}>
            {current.sku} · {current.productName}
          </Text>
          {current.kitSku ? <Text style={styles.help}>{t('salesDispatch.kitPart', { kit: current.kitSku, per: f.qty(current.perKit ?? 1) })}</Text> : null}
          {current.lotNumber ? <Text style={styles.help}>{t('dispatch.lotNumber', { lot: current.lotNumber })}</Text> : null}
          <Text style={styles.label}>{t('salesDispatch.qtyToTake', { qty: f.qty(current.qty) })}</Text>
          <KeyboardInput
            value={qtyText}
            onChangeText={(v) => {
              setBinError(null)
              setQtyText(v)
            }}
            keyboardType="decimal-pad"
            style={styles.input}
            accessibilityLabel={t('dispatch.qtyLabel')}
            testID="sales-qty"
          />
          <ScanField
            label={t('salesDispatch.scanBin')}
            help={t('salesDispatch.scanBinHelp', { bin: current.binCode })}
            suggestedValue={current.binCode}
            autoFocus={false}
            error={binError}
            onSubmit={(code) => void scanBin(current, code)}
            pick="bin"
            testID="sales-scan-bin"
          />
          <BigButton label={t('salesDispatch.skip')} variant="secondary" onPress={() => skip(current)} testID="sales-skip" />
        </View>
      ) : doneAll ? (
        <Text style={styles.okText}>{t('salesDispatch.allDone')}</Text>
      ) : null}

      {picks.length > 0 ? <Text style={styles.label}>{t('salesDispatch.taken')}</Text> : null}
      {picks.map((p) => {
        const task = tasks.find((x) => x.key === p.taskKey)
        return (
          <Pressable
            key={p.taskKey}
            accessibilityRole="button"
            accessibilityLabel={t('salesDispatch.undoA11y', { sku: task?.sku ?? '', bin: p.binCode })}
            onPress={() => undo(p.taskKey)}
            style={({ pressed }) => [styles.doneRow, pressed && styles.cardPressed]}
          >
            <Text style={styles.label}>
              ✓ {f.qty(p.qty)} × {task?.sku} · {p.binCode}
            </Text>
            <Text style={styles.help}>{t('salesDispatch.tapToUndo')}</Text>
          </Pressable>
        )
      })}

      <BigButton label={t('salesDispatch.finish')} onPress={() => setStep('finish')} disabled={picks.length === 0} testID="sales-finish" />
      <BigButton
        label={t('salesDispatch.cancel')}
        variant="danger"
        onPress={() => {
          setOrder(null)
          setPicks([])
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
  okText: { color: colors.brand, fontSize: fontSize.message, fontWeight: '700' },
  field: { gap: spacing.xs },
  bottom: { marginTop: 'auto' },
  card: { gap: 2, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.panelAlt },
  cardPressed: { backgroundColor: colors.panel },
  cardTitle: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  current: { gap: spacing.sm, padding: spacing.md, borderRadius: 12, borderWidth: 2, borderColor: colors.brand, backgroundColor: colors.panelAlt },
  doneRow: { gap: 2, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line },
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
