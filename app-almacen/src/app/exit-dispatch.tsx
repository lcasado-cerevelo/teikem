// Lote 146 — despachar contra un aviso de salida en el aparato: la lista de avisos con algo reservado, el escaneo del número (SAL-…) y el despacho GUIADO por lo que el sistema reservó
// (posición → cantidad), igual que el de órdenes de venta pero sin kits, consignatario ni entrega. Va por la cola de salida (sirve sin señal); el servidor valida la existencia
// de la posición y que no se despache más de lo reservado al sincronizar.
import { useEffect, useMemo, useRef, useState } from 'react'
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'
import { useLocalSearchParams, useRouter } from 'expo-router'

import { useFormat } from '../kernel/format/useFormat'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { resolveBinLocalFirst } from '../kernel/warehouse/binLookup'
import { useT } from '../kernel/i18n/useT'
import { downloadExitNoticesIfAllowed, refreshAssignments } from '../kernel/sync/download'
import { flushNow } from '../kernel/sync/engine'
import { discardRow, outboxResult } from '../kernel/sync/outbox'
import { BigButton } from '../kernel/ui/BigButton'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { ScanField } from '../kernel/ui/ScanField'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { findExitNoticeByNumber, getExitNoticeLines, getGroupTasks, listExitNotices, listGroupableOrders, queueExitDispatch, queueExitGroup, type LocalExitNotice } from '../features/exitDispatch/localExit'
import { dispatchScope } from '../features/dispatch/dispatchScope'
import { useMyPermissions } from '../kernel/auth/permissions'
import { useSession } from '../kernel/auth/useSession'
import { buildGroupBody, docKey, groupBlock, groupNumber, groupReady, MIN_GROUP } from '../features/exitDispatch/exitGroupLogic'
import { mineFirst, takeDispatchIfFree } from '../features/dispatch/dispatchTake'
import { TaskChooser } from '../features/salesDispatch/TaskChooser'
import {
  buildTasks,
  checkPickQty,
  dispatchBatchNumber,
  currentTask,
  nextTask,
  parseQty,
  totalPicked,
  type SalesPick,
  type SalesTask,
} from '../features/salesDispatch/salesDispatchLogic'

type Step = 'list' | 'pick' | 'finish' | 'sending'

export default function ExitDispatchScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  // Lote 172: «Mis tareas» abre el aviso asignado con ?notice=SAL-##### (una sola vez; como si se hubiera escaneado)
  const { notice: noticeParam } = useLocalSearchParams<{ notice?: string }>()
  const noticeOpened = useRef(false)
  const warehousePublicId = useActiveWarehouse().publicId
  // Lote 193: sin warehouse.pick.any solo se ven los avisos asignados a esta persona (y no hay órdenes de venta)
  const permissions = useMyPermissions()
  const { session } = useSession()
  const scope = useMemo(() => dispatchScope(permissions, session?.userId), [permissions, session?.userId])
  const [step, setStep] = useState<Step>('list')
  const [tick, setTick] = useState(0)
  const [notice, setNotice] = useState<LocalExitNotice | null>(null)
  const [picks, setPicks] = useState<SalesPick[]>([])
  const [skipped, setSkipped] = useState<Set<string>>(new Set())
  const [qtyText, setQtyText] = useState('')
  const [focusKey, setFocusKey] = useState<string | null>(null)   // Lote 192: la tarea que el operario eligió de la lista
  const [scanError, setScanError] = useState<string | null>(null)
  const [binError, setBinError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const [showFree, setShowFree] = useState(false)
  // Lote 178: «Recolectar juntos» — avisos elegidos (modo selección) y, ya en la ruta, el grupo que se recolecta
  const [choosing, setChoosing] = useState(false)
  const [chosen, setChosen] = useState<LocalExitNotice[]>([])
  const [group, setGroup] = useState<LocalExitNotice[] | null>(null)
  const lock = useRef(false)

  // tick fuerza releer la copia local tras volver de enviar.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const noticesRaw = useMemo(() => (warehousePublicId ? listExitNotices(warehousePublicId, scope) : []), [warehousePublicId, tick, scope])
  // Lote 186: en «Recolectar juntos» también se eligen órdenes de venta (se ven solo al elegir)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const ordersRaw = useMemo(() => (warehousePublicId ? listGroupableOrders(warehousePublicId, scope) : []), [warehousePublicId, tick, scope])
  const myId = session?.userId ?? null
  // Lote 198: avisos y órdenes en una sola lista, lo asignado a esta persona primero
  const notices = useMemo(() => mineFirst(noticesRaw, myId), [noticesRaw, myId])
  const orders = useMemo(() => mineFirst(ordersRaw, myId), [ordersRaw, myId])
  const everyDoc = useMemo(() => mineFirst([...noticesRaw, ...ordersRaw], myId), [noticesRaw, ordersRaw, myId])
  // Lote 198: primero lo asignado a esta persona; lo que nadie tiene asignado se ve con la opción de abajo
  const freeCount = everyDoc.filter((d) => d.assignedToUserId == null).length
  const allDocs = everyDoc.filter((d) => (d.assignedToUserId != null && d.assignedToUserId === myId) || (showFree && d.assignedToUserId == null))
  const lines = useMemo(() => (notice ? getExitNoticeLines(notice.id) : []), [notice])
  const groupData = useMemo(() => (group ? getGroupTasks(group) : null), [group])
  const tasks = useMemo(() => (groupData ? groupData.tasks : buildTasks(lines)), [lines, groupData])
  const current = notice && step === 'pick' ? currentTask(tasks, picks, skipped, focusKey) : null
  const doneAll = step === 'pick' && notice !== null && current === null

  useEffect(() => {
    if (!warehousePublicId) return undefined
    let alive = true
    void refreshAssignments(warehousePublicId).then(() => {
      if (alive) setTick((x) => x + 1)
    })
    return () => {
      alive = false
    }
  }, [warehousePublicId])

  useEffect(() => {
    if (noticeOpened.current || !noticeParam || !warehousePublicId) return
    noticeOpened.current = true
    scanNotice(noticeParam)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [noticeParam, warehousePublicId])

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function open(o: LocalExitNotice) {
    // una orden de venta tiene su propia pantalla (kits, consignatario y entrega)
    if (o.kind === 'SALES_ORDER') {
      void takeDispatchIfFree(o, myId)
      router.push({ pathname: '/sales-dispatch', params: { order: o.number } })
      return
    }
    void takeDispatchIfFree(o, myId)
    setGroup(null)
    setNotice(o)
    setPicks([])
    setSkipped(new Set())
    setBinError(null)
    setScanError(null)
    setInfo(null)
    setStep('pick')
    const first = nextTask(buildTasks(getExitNoticeLines(o.id)), [], new Set())
    setQtyText(first ? String(first.qty) : '')
    vibrateOk()
  }

  function toggleChosen(o: LocalExitNotice) {
    setScanError(null)
    setChosen((cur) => (cur.some((c) => docKey(c) === docKey(o)) ? cur.filter((c) => docKey(c) !== docKey(o)) : groupBlock(o, cur) ? cur : [...cur, o]))
  }

  /** Empieza la ruta guiada de todos los avisos elegidos juntos. */
  function startGroup() {
    if (chosen.length < MIN_GROUP) return
    for (const c of chosen) void takeDispatchIfFree(c, myId)
    const data = getGroupTasks(chosen)
    setGroup(chosen)
    setNotice(chosen[0])
    setPicks([])
    setSkipped(new Set())
    setBinError(null)
    setScanError(null)
    setInfo(null)
    setChoosing(false)
    setStep('pick')
    const first = nextTask(data.tasks, [], new Set())
    setQtyText(first ? String(first.qty) : '')
    vibrateOk()
  }

  function scanNotice(code: string) {
    const found = findExitNoticeByNumber(warehousePublicId!, code, scope)
    if (!found || found.reserved <= 0) {
      setScanError(t('exitDispatch.noticeNotFound'))
      vibrateError()
      return
    }
    open(found)
  }

  function advance(nextPicks: SalesPick[], nextSkipped: Set<string>) {
    setFocusKey(null)
    setPicks(nextPicks)
    setSkipped(nextSkipped)
    const n = nextTask(tasks, nextPicks, nextSkipped)
    setQtyText(n ? String(n.qty) : '')
    setBinError(null)
  }

  /** Lee la posición de la tarea actual: la reservada o cualquier otra (la reserva se mueve ahí si tiene existencia; con la rotación obligatoria el servidor lo rechaza). */
  async function scanBin(task: SalesTask, code: string) {
    const block = checkPickQty(task, qtyText)
    if (block) {
      setBinError(t(block === 'qtyInvalid' ? 'exitDispatch.qtyInvalid' : 'exitDispatch.qtyTooMuch', { qty: f.qty(task.qty) }))
      vibrateError()
      return
    }
    const bin = await resolveBinLocalFirst(warehousePublicId!, code).catch(() => null)
    if (!bin) {
      setBinError(t('exitDispatch.binNotFound'))
      vibrateError()
      return
    }
    const qty = parseQty(qtyText) ?? 0
    advance([...picks, { taskKey: task.key, binId: bin.id, binCode: bin.code, qty }], skipped)
    setInfo(bin.id !== task.binId ? t('exitDispatch.binMoves', { bin: bin.code }) : null)
    vibrateOk()
  }

  function skip(task: SalesTask) {
    advance(picks, new Set([...skipped, task.key]))
  }

  function undo(key: string) {
    advance(
      picks.filter((p) => p.taskKey !== key),
      new Set([...skipped].filter((k) => k !== key)),
    )
  }

  async function send() {
    if (!notice || lock.current) return
    lock.current = true
    setStep('sending')
    let id = 0
    try {
      if (group && groupData) {
        const queued = queueExitGroup(group, tasks, picks, groupData.lineNotice)
        if (queued === null) throw new Error('group')
        id = queued
      } else {
        id = queueExitDispatch(notice, tasks, picks)
      }
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
      // el servidor ya deshizo los saldos (runOutbox); la fila no queda en Sincronización: se muestra el mensaje exacto y se baja el aviso como está
      discardRow(id)
      void downloadExitNoticesIfAllowed(warehousePublicId!).catch(() => undefined)
      setNotice(null)
      setGroup(null)
      setChosen([])
      setPicks([])
      setStep('list')
      setScanError(result.error || t('errors.generic'))
      setInfo(null)
      setTick((x) => x + 1)
      vibrateError()
      return
    }
    const number = result.status === 'sent' ? (group ? groupNumber(outboxResult(id)) : dispatchBatchNumber(outboxResult(id))) : null
    setInfo(result.status === 'sent' ? (number ? t(group ? 'exitDispatch.groupSent' : 'exitDispatch.sent', { number }) : t('exitDispatch.sentNoNumber')) : t('exitDispatch.queued'))
    setScanError(null)
    setGroup(null)
    setChosen([])
    setNotice(null)
    setPicks([])
    setStep('list')
    setTick((x) => x + 1)
    vibrateOk()
  }

  if (step === 'sending') {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('exitDispatch.title')}</Text>
        <ActivityIndicator color={colors.brand} />
        <Text style={styles.help}>{t('exitDispatch.sending')}</Text>
      </KeyboardScreen>
    )
  }

  if (step === 'list' || !notice) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('exitDispatch.title')}</Text>
        <ScanField label={t('exitDispatch.scanNotice')} help={t('exitDispatch.scanNoticeHelp')} error={scanError} notice={info} onSubmit={scanNotice} testID="exit-scan-notice" />
        {allDocs.length === 0 ? <Text style={styles.help}>{t(everyDoc.length === 0 ? 'exitDispatch.noNotices' : 'exitDispatch.noneMine')}</Text> : null}
        {allDocs.length >= MIN_GROUP ? (
          <BigButton
            label={choosing ? t('exitDispatch.groupCancel') : t('exitDispatch.groupStart')}
            variant="secondary"
            onPress={() => {
              setChoosing((v) => !v)
              setChosen([])
            }}
            testID="exit-group-toggle"
          />
        ) : null}
        {choosing ? <Text style={styles.help}>{t('exitDispatch.groupHelp')}</Text> : null}
        {allDocs.map((o) => {
          const on = chosen.some((c) => docKey(c) === docKey(o))
          const blocked = choosing && !on && groupBlock(o, chosen) !== null
          const isOrder = o.kind === 'SALES_ORDER'
          return (
          <Pressable
            key={docKey(o)}
            accessibilityRole={choosing ? 'checkbox' : 'button'}
            accessibilityState={choosing ? { checked: on, disabled: blocked } : undefined}
            accessibilityLabel={t(choosing ? (isOrder ? 'exitDispatch.groupPickOrder' : 'exitDispatch.groupPick') : 'exitDispatch.openNotice', { number: o.number })}
            onPress={() => (choosing ? (blocked ? setScanError(t('exitDispatch.groupOtherClient')) : toggleChosen(o)) : open(o))}
            style={({ pressed }) => [styles.card, pressed && styles.cardPressed, on && styles.cardOn, blocked && styles.cardDim]}
            testID={isOrder ? `exit-order-${o.number}` : `exit-notice-${o.number}`}
          >
            <Text style={styles.cardTitle}>{choosing ? (on ? '☑ ' : '☐ ') : ''}{o.number}{isOrder ? ' · ' + t('exitDispatch.orderTag') : ''}</Text>
            <Text style={styles.help}>{[o.clientName, o.destination ?? o.reference].filter(Boolean).join(' · ')}</Text>
            <Text style={o.assignedToUserId != null && o.assignedToUserId === myId ? styles.mine : styles.help}>
              {t(o.assignedToUserId == null ? 'exitDispatch.unassigned' : o.assignedToUserId === myId ? 'exitDispatch.assignedToYou' : 'exitDispatch.assignedOther')}
            </Text>
            <Text style={styles.help}>
              {t('exitDispatch.toDispatch', { qty: f.qty(o.reserved) })}
              {o.requiredDate ? ' · ' + t('exitDispatch.required', { date: f.date(o.requiredDate) }) : ''}
            </Text>
          </Pressable>
          )
        })}
        {freeCount > 0 ? (
          <BigButton label={showFree ? t('myTasks.hideFree') : t('myTasks.showFree', { count: freeCount })} variant="secondary" onPress={() => setShowFree((v) => !v)} testID="exit-free-toggle" />
        ) : null}
        {choosing ? (
          <BigButton label={t('exitDispatch.groupGo', { n: chosen.length })} onPress={startGroup} disabled={chosen.length < MIN_GROUP} testID="exit-group-go" />
        ) : null}
        <BigButton label={t('exitDispatch.noDocument')} variant="secondary" onPress={() => router.replace('/dispatch')} testID="exit-no-document" />
        <View style={styles.bottom}>
          <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
        </View>
      </KeyboardScreen>
    )
  }

  if (step === 'finish') {
    const total = totalPicked(picks)
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('exitDispatch.finishTitle', { number: group ? group.map((g) => g.number).join(' + ') : notice.number })}</Text>
        <Text style={styles.label}>{t('exitDispatch.finishSummary', { lines: picks.length, qty: f.qty(total) })}</Text>
        <Text style={styles.help}>{t('exitDispatch.finishHelp')}</Text>
        {scanError ? <Text style={styles.error}>{scanError}</Text> : null}
        {group && groupData && !groupReady(buildGroupBody(group, tasks, picks, groupData.lineNotice, notice.warehousePublicId)) ? <Text style={styles.error}>{t('exitDispatch.groupNeedTwo')}</Text> : null}
        <BigButton
          label={t('exitDispatch.send')}
          onPress={() => void send()}
          disabled={group !== null && groupData !== null && !groupReady(buildGroupBody(group, tasks, picks, groupData.lineNotice, notice.warehousePublicId))}
          testID="exit-send"
        />
        <BigButton label={t('common.back')} variant="secondary" onPress={() => setStep('pick')} />
      </KeyboardScreen>
    )
  }

  // step === 'pick'
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{group ? group.map((g) => g.number).join(' + ') : notice.number}</Text>
      <Text style={styles.help}>{group ? t('exitDispatch.groupBanner', { client: notice.clientName }) : [notice.clientName, notice.destination ?? notice.reference].filter(Boolean).join(' · ')}</Text>
      {info ? <Text style={styles.okText}>{info}</Text> : null}

      {current ? (
        <View style={styles.current} testID="exit-current">
          <Text style={styles.cardTitle}>{t('exitDispatch.goTo', { bin: current.binCode })}</Text>
          <Text style={styles.label}>
            {current.sku} · {current.productName}
          </Text>
          {current.lotNumber ? <Text style={styles.help}>{t('dispatch.lotNumber', { lot: current.lotNumber })}</Text> : null}
          <Text style={styles.label}>{t('exitDispatch.qtyToTake', { qty: f.qty(current.qty) })}</Text>
          <KeyboardInput
            value={qtyText}
            onChangeText={(v) => {
              setBinError(null)
              setQtyText(v)
            }}
            keyboardType="decimal-pad"
            style={styles.input}
            accessibilityLabel={t('dispatch.qtyLabel')}
            testID="exit-qty"
          />
          <ScanField
            label={t('exitDispatch.scanBin')}
            help={t('exitDispatch.scanBinHelp', { bin: current.binCode })}
            suggestedValue={current.binCode}
            autoFocus={false}
            error={binError}
            onSubmit={(code) => void scanBin(current, code)}
            pick="bin"
            testID="exit-scan-bin"
          />
          <BigButton label={t('exitDispatch.skip')} variant="secondary" onPress={() => skip(current)} testID="exit-skip" />
        </View>
      ) : doneAll ? (
        <Text style={styles.okText}>{t('exitDispatch.allDone')}</Text>
      ) : null}

      <TaskChooser
        tasks={tasks}
        picks={picks}
        skipped={skipped}
        currentKey={current?.key ?? null}
        onChoose={(task) => {
          setFocusKey(task.key)
          setQtyText(String(task.qty))
          setBinError(null)
        }}
      />

      {picks.length > 0 ? <Text style={styles.label}>{t('exitDispatch.taken')}</Text> : null}
      {picks.map((p) => {
        const task = tasks.find((x) => x.key === p.taskKey)
        return (
          <Pressable
            key={p.taskKey}
            accessibilityRole="button"
            accessibilityLabel={t('exitDispatch.undoA11y', { sku: task?.sku ?? '', bin: p.binCode })}
            onPress={() => undo(p.taskKey)}
            style={({ pressed }) => [styles.doneRow, pressed && styles.cardPressed]}
          >
            <Text style={styles.label}>
              ✓ {f.qty(p.qty)} × {task?.sku} · {p.binCode}
            </Text>
            <Text style={styles.help}>{t('exitDispatch.tapToUndo')}</Text>
          </Pressable>
        )
      })}

      <BigButton label={t('exitDispatch.finish')} onPress={() => setStep('finish')} disabled={picks.length === 0} testID="exit-finish" />
      <BigButton
        label={t('exitDispatch.cancel')}
        variant="danger"
        onPress={() => {
          setNotice(null)
          setGroup(null)
          setChosen([])
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
  mine: { color: colors.brand, fontSize: fontSize.message, fontWeight: '600' },
  bottom: { marginTop: 'auto' },
  card: { gap: 2, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.panelAlt },
  cardPressed: { backgroundColor: colors.panel },
  cardOn: { borderColor: colors.brand, borderWidth: 2 },
  cardDim: { opacity: 0.45 },
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
