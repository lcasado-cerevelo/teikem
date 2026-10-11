import { useMemo, useRef, useState } from 'react'
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, TextInput, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError } from '../kernel/api/client'
import { useFormat } from '../kernel/format/useFormat'
import { useMyPermissions } from '../kernel/auth/permissions'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useT } from '../kernel/i18n/useT'
import { flushNow, runSync } from '../kernel/sync/engine'
import { discardRow, outboxResult } from '../kernel/sync/outbox'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { BinMarkList } from '../features/positions/BinMarkList'
import { capMarks, markRecommended, marksToRows, toggleMark, type MarkOption, type Marks } from '../features/positions/binMarks'
import { fetchClientsForOwnDispatch, fetchConsigneesForClient, fetchStockOptions, queueManualIssue, resolveBinCodes, submitCollectAndPack } from '../features/dispatch/dispatchApi'
import { addLocalPickLine, discardLocalPick, getOpenPick, removeLocalPickLine, restoreLocalPick, startLocalPick } from '../features/dispatch/localPick'
import {
  initialReason,
  issueTotals,
  MANUAL_ISSUE_NOTE_MAX,
  manualIssueBlock,
  manualIssueNumber,
  reasonOptions,
  WAREHOUSE_ISSUE,
  type ReasonSource,
} from '../features/dispatch/manualIssueLogic'
import { readLastReason, readManualIssueReasons, saveLastReason } from '../features/dispatch/manualIssueReasons'
import {
  availableAfterPicked,
  binScanOutcome,
  checkLotBin,
  nextStockOption,
  pickedQty,
  planExit,
  stockKey,
  pickQtyState,
  type StockOption,
  type ClientChoice,
  type ConsigneeChoice,
  newPickLineDraft,
  sameOwner,
  type PickLine,
  type PickLineDraft,
  type ResolvedPickLine,
} from '../features/dispatch/dispatchLogic'

/** De dónde dice el sistema que puede salir el producto escaneado (existencia disponible por posición y lote). `options` null = todavía
 *  buscando; `offline` = no se pudo preguntar (sin señal): no hay sugerencia ni se exige posición. */
interface StockHint {
  productPublicId: string
  lot: boolean
  options: StockOption[] | null
  /** `device` = sin señal, con la copia bajada al aparato; `none` = sin señal y sin copia (no hay sugerencia ni se exige posición). */
  source: 'server' | 'device' | 'none' | null
}

// 'reason' (2026-10-11): «Completar despacho» = despacho manual. Desde 2026-10-11 (b) abre directo la confirmación con el motivo ya puesto (el que ya
// estaba, el último usado en el aparato o el default de la compañía) y solo muestra la lista si no hay ninguno o se toca «Cambiar».
type Step = { name: 'scan' } | { name: 'client' } | { name: 'consignee' } | { name: 'error'; message: string } | { name: 'reason'; lines: ResolvedPickLine[] }

/** Pantalla 5 (docs/mobile/app-almacen-plan.md §2): recolectar (producto, cantidad, posición) sin señal; empacar
 *  (elegir consignatario y confirmar) necesita señal un momento y luego se manda por la cola. Solo clientes 3PL por
 *  ahora (docs/lote8A-app-decisiones.md): el dueño del producto ya viene sincronizado. Inventario propio (2026-10-05): se
 *  despacha igual; al empacar se elige primero el cliente a quien se despacha y luego su consignatario.
 *  2026-10-11 (decisión del dueño): «Completar despacho» es el DESPACHO MANUAL (DMA-#####): salida sin entrega con motivo obligatorio y nota
 *  opcional, solo con el permiso warehouse.issue; va a la cola de salida (kind 'manualIssue') como Transferir y Ajustar. «Empacar» no cambia.
 *  2026-10-11 (b) (decisión del dueño: no complicar el aparato): con motivo por default o último usado, completar = 2 toques (Completar → Despachar),
 *  sin teclado; la nota queda detrás de «Agregar nota». */
export default function DispatchScreen() {
  const { t, lang } = useT()
  const router = useRouter()
  const activeWarehouse = useActiveWarehouse()
  const warehousePublicId = activeWarehouse.publicId
  const [tick, setTick] = useState(0)
  const [scanError, setScanError] = useState<string | null>(null)
  const [draft, setDraft] = useState<PickLineDraft | null>(null)
  const [packing, setPacking] = useState<Step>({ name: 'scan' })
  const [consignees, setConsignees] = useState<ConsigneeChoice[] | null>(null)
  // inventario propio: clientes a elegir y el elegido (con inventario 3PL el cliente es el dueño)
  const [clients, setClients] = useState<ClientChoice[] | null>(null)
  const [chosenClient, setChosenClient] = useState<ClientChoice | null>(null)
  const [pieces, setPieces] = useState('1')
  const [busy, setBusy] = useState(false)
  // aviso verde de la última línea agregada (se queda hasta la siguiente lectura)
  const [notice, setNotice] = useState<string | null>(null)
  // aviso rojo de la lectura de la posición (sin cantidad o cantidad inválida); se quita al escribir la cantidad
  const [binError, setBinError] = useState<string | null>(null)
  const qtyRef = useRef<TextInput>(null)
  const f = useFormat()
  const [hint, setHint] = useState<StockHint | null>(null)
  // Listado de posiciones marcables (unido al plan de salida): lo marcado, si se ve aunque la cantidad quepa en la sugerida, y el valor que
  // la sugerida (o una fila) pone en el campo de la posición.
  const [marks, setMarks] = useState<Marks>({})
  const [showAll, setShowAll] = useState(false)
  const [prefill, setPrefill] = useState<{ value: string; seq: number } | null>(null)
  // Despacho manual: el motivo escogido (y de dónde salió), la nota (escondida tras «Agregar nota»), si se está viendo la lista de motivos, el aviso rojo
  // del paso y «enviando» (el despacho local ya se cerró y se espera al servidor como mucho unos segundos). El motivo y la nota se conservan si el
  // servidor lo rechaza, para corregir y volver a intentar.
  const permissions = useMyPermissions()
  const canIssue = permissions?.includes(WAREHOUSE_ISSUE) ?? false
  const [reasonCode, setReasonCode] = useState<string | null>(null)
  const [issueNote, setIssueNote] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [reasonSource, setReasonSource] = useState<ReasonSource | 'picked' | null>(null)
  const [pickingReason, setPickingReason] = useState(false)
  const [showNote, setShowNote] = useState(false)
  const [sending, setSending] = useState(false)
  // 2026-10-11 (b): sin el Alert, «Despachar» encola al primer toque; este candado evita que un doble toque encole dos despachos (dos DMA)
  const submitLock = useRef(false)

  // tick fuerza releer la base local tras cada mutación; getOpenPick() no usa tick.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const openPick = useMemo(() => getOpenPick(), [tick])
  const refresh = () => setTick((n) => n + 1)

  // La sugerencia: el primer lugar de salida con disponible tras descontar lo que este despacho ya sacó de ese producto.
  const suggestion =
    draft && hint && hint.productPublicId === draft.productPublicId && hint.options
      ? nextStockOption(hint.options, pickedQty(openPick?.lineRows ?? [], draft.productPublicId))
      : null
  // El listado: las existencias con disponible (descontando lo que este despacho ya sacó), cada una con su tope; las recomendadas son las que el orden de
  // salida usaría para completar la cantidad escrita. Con lote no hay listado: la posición la manda el FEFO.
  const typedQty = draft && pickQtyState(draft.qtyText) === 'ok' ? Number(draft.qtyText.trim().replace(',', '.')) : 0
  const alreadyPicked = draft ? pickedQty(openPick?.lineRows ?? [], draft.productPublicId) : 0
  const plan =
    draft && hint && !hint.lot && hint.productPublicId === draft.productPublicId && hint.options && typedQty > 0 ? planExit(hint.options, typedQty, alreadyPicked) : null
  const markOptions: MarkOption[] | null =
    plan && hint?.options
      ? availableAfterPicked(hint.options, alreadyPicked).map((o) => ({
          key: stockKey(o),
          binCode: o.binCode,
          capacity: o.available,
          recommended: plan.rows.some((r) => stockKey(r) === stockKey(o)),
          detail: stockDetail(o),
        }))
      : null
  // si la cantidad bajó o cambió, las marcas se ajustan solas
  const effectiveMarks = markOptions ? capMarks(markOptions, marks, typedQty) : {}
  // la cantidad no cabe en la sugerida: hace falta más de una posición
  const doesNotFit = Boolean(suggestion && typedQty > suggestion.available)
  const listVisible = Boolean(markOptions && markOptions.length > 0 && (doesNotFit || showAll))
  /** "lote L-3, vence 12/31/2026, disponible 40" (solo lo que existe). */
  function stockDetail(o: StockOption): string {
    return [
      o.lotNumber ? t('dispatch.lotNumber', { lot: o.lotNumber }) : null,
      o.expiryDate ? t('dispatch.expires', { date: f.date(o.expiryDate) }) : null,
      t('dispatch.availableQty', { qty: f.qty(o.available) }),
    ]
      .filter(Boolean)
      .join(' · ')
  }

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function scanProduct(code: string) {
    setNotice(null)
    const product = findProductByCode(code)
    if (!product) {
      setScanError(t('dispatch.productNotFound'))
      vibrateError()
      return
    }
    if (openPick && !sameOwner(openPick.clientPublicId, product.ownerClientPublicId)) {
      setScanError(t('dispatch.mixedOwners'))
      vibrateError()
      return
    }
    if (!openPick) {
      startLocalPick(warehousePublicId!, product.ownerClientPublicId ? { publicId: product.ownerClientPublicId, name: product.ownerName ?? '' } : null)
    }
    setScanError(null)
    setBinError(null)
    setMarks({})
    setShowAll(false)
    setDraft(newPickLineDraft(product))
    void loadHint(product.publicId, product.trackingTypeCode === 'LOT')
    vibrateOk()
    refresh()
  }

  /** Posición sugerida: el orden de salida del servidor (en línea, o la copia del aparato sin señal). Sin ninguna de las dos, sin sugerencia. */
  async function loadHint(productPublicId: string, lot: boolean) {
    setHint({ productPublicId, lot, options: null, source: null })
    const result = await fetchStockOptions(warehousePublicId!, productPublicId)
    setHint((h) => (h && h.productPublicId === productPublicId ? { ...h, options: result.options, source: result.source } : h))
  }

  function addLine(line: PickLine) {
    if (!openPick) return
    addLocalPickLine(openPick.id, line)
    setDraft(null)
    setHint(null)
    setMarks({})
    setShowAll(false)
    setBinError(null)
    setNotice(t('dispatch.lineAdded', { qty: line.quantity, sku: line.sku, bin: line.fromBinCode }))
    vibrateOk()
    refresh()
  }

  /** Posición de donde sale (decisión del dueño 5: la cantidad va primero). Con cantidad > 0 la lectura agrega la línea
   *  (escanear = Aceptar); sin cantidad o con una inválida no agrega nada, avisa y deja el cursor en la cantidad para
   *  escribirla y volver a escanear. La posición no se guarda: no queda una línea a medias. */
  function scanFromBin(code: string) {
    if (!draft) return
    // Producto con lote: la posición es la del próximo lote en salir (FEFO), no se escoge (pedido del dueño 2026-10-05)
    if (hint?.lot && suggestion && pickQtyState(draft.qtyText) === 'ok') {
      const check = checkLotBin(suggestion, code, Number(draft.qtyText.trim().replace(',', '.')))
      if (check.kind !== 'ok') {
        setBinError(
          check.kind === 'otherBin'
            ? t('dispatch.lotMustUse', { bin: suggestion.binCode, detail: stockDetail(suggestion) })
            : t('dispatch.lotTooMuch', { bin: suggestion.binCode, qty: f.qty(suggestion.available) }),
        )
        vibrateError()
        return
      }
    }
    const outcome = binScanOutcome(draft, code)
    if (outcome.kind === 'add') {
      addLine(outcome.line)
      return
    }
    if (outcome.kind === 'noBin') return
    setBinError(t(outcome.kind === 'needQty' ? 'dispatch.qtyFirst' : 'dispatch.qtyInvalid'))
    vibrateError()
    qtyRef.current?.focus()
  }

  /** Usa las posiciones marcadas: agrega una línea por posición, de una vez. */
  function addMarkedLines() {
    if (!draft || !openPick || !markOptions) return
    const rows = marksToRows(markOptions, effectiveMarks)
    if (rows.length === 0) return
    for (const r of rows) {
      addLocalPickLine(openPick.id, { productPublicId: draft.productPublicId, sku: draft.sku, productName: draft.productName, quantity: r.qty, fromBinCode: r.binCode })
    }
    setDraft(null)
    setHint(null)
    setMarks({})
    setShowAll(false)
    setBinError(null)
    setNotice(t('dispatch.planAdded', { sku: draft.sku, count: rows.length }))
    vibrateOk()
    refresh()
  }

  /** Toca la sugerida: si la cantidad cabe, su posición se pone en el campo (queda listo Aceptar); si no cabe, se marca en el listado. */
  function tapSuggestion() {
    if (!suggestion) return
    if (doesNotFit && markOptions) {
      setMarks(toggleMark(markOptions, effectiveMarks, stockKey(suggestion), typedQty))
      return
    }
    setBinError(null)
    setPrefill((p) => ({ value: suggestion.binCode, seq: (p?.seq ?? 0) + 1 }))
  }

  function cancelDispatch() {
    Alert.alert(t('dispatch.cancelConfirmTitle'), t('dispatch.cancelConfirmBody'), [
      { text: t('common.no'), style: 'cancel' },
      {
        text: t('dispatch.cancelDispatch'),
        style: 'destructive',
        onPress: () => {
          discardLocalPick()
          setChosenClient(null)
          setDraft(null)
          setHint(null)
          setPacking({ name: 'scan' })
          refresh()
        },
      },
    ])
  }

  async function startPacking() {
    if (!openPick || openPick.lineRows.length === 0) return
    setBusy(true)
    try {
      const resolved = await resolveBinCodes(warehousePublicId!, openPick.lineRows)
      if (resolved.notFound.length > 0) {
        setPacking({ name: 'error', message: t('putaway.binNotFound') + ' ' + resolved.notFound.join(', ') })
        return
      }
      if (openPick.clientPublicId) {
        setConsignees(await fetchConsigneesForClient(openPick.clientPublicId))
        setPacking({ name: 'consignee' })
      } else {
        // inventario propio: primero a qué cliente se despacha
        setClients(await fetchClientsForOwnDispatch())
        setPacking({ name: 'client' })
      }
    } catch (err) {
      setPacking({ name: 'error', message: err instanceof ApiError ? err.title : t('dispatch.consigneesError') })
    } finally {
      setBusy(false)
    }
  }

  async function chooseClient(client: ClientChoice) {
    setBusy(true)
    try {
      setConsignees(await fetchConsigneesForClient(client.publicId))
      setChosenClient(client)
      setPacking({ name: 'consignee' })
    } catch (err) {
      setPacking({ name: 'error', message: err instanceof ApiError ? err.title : t('dispatch.consigneesError') })
    } finally {
      setBusy(false)
    }
  }

  /** «Completar despacho» = despacho manual (2026-10-11): primero resuelve las posiciones (las del aparato, sin señal; si no está, el servidor) y luego
   *  abre la confirmación con el motivo ya puesto (2026-10-11 (b): initialReason); sin ninguno, la lista para escogerlo. Una posición que no existe no
   *  deja seguir y lo dice aquí mismo. */
  async function startManualIssue() {
    if (!openPick || openPick.lineRows.length === 0 || !canIssue) return
    setBusy(true)
    try {
      const resolved = await resolveBinCodes(warehousePublicId!, openPick.lineRows)
      if (resolved.notFound.length > 0) {
        setScanError(t('putaway.binNotFound') + ' ' + resolved.notFound.join(', '))
        vibrateError()
        return
      }
      setScanError(null)
      setReasonError(null)
      const init = initialReason(readManualIssueReasons(), reasonCode, readLastReason())
      setReasonCode(init?.code ?? null)
      setReasonSource(init?.source ?? null)
      setPickingReason(init === null)
      setShowNote(issueNote.length > 0)
      setPacking({ name: 'reason', lines: resolved.lines })
    } catch (err) {
      setScanError(err instanceof ApiError ? err.title : t('errors.generic'))
      vibrateError()
    } finally {
      setBusy(false)
    }
  }

  /** «Despachar» de la confirmación: revisa el motivo y la nota y encola (2026-10-11 (b): esta pantalla ya es la confirmación, no hay otro aviso). */
  function confirmManualIssue(lines: ResolvedPickLine[]) {
    const block = manualIssueBlock(reasonCode, issueNote)
    if (block) {
      setReasonError(t(block === 'reason' ? 'dispatch.reasonRequired' : 'dispatch.noteTooLong', { max: MANUAL_ISSUE_NOTE_MAX }))
      if (block === 'reason') setPickingReason(true)
      vibrateError()
      return
    }
    setReasonError(null)
    void submitManualIssue(lines)
  }

  /** Toca un motivo de la lista: queda escogido y se vuelve a la confirmación. */
  function pickReason(code: string) {
    setReasonCode(code)
    setReasonSource('picked')
    setReasonError(null)
    setPickingReason(false)
  }

  /** Encola el despacho manual (cierra el despacho local y resta del saldo local en el mismo paso) y espera al servidor como mucho unos segundos:
   *  enviado → aviso con el número DMA; sin señal → «en cola»; rechazado → se quita de la cola, el despacho vuelve a abrirse tal cual y se muestra
   *  el mensaje exacto del servidor. */
  async function submitManualIssue(lines: ResolvedPickLine[]) {
    if (submitLock.current) return
    submitLock.current = true
    try {
      await sendManualIssue(lines)
    } finally {
      submitLock.current = false
    }
  }

  async function sendManualIssue(lines: ResolvedPickLine[]) {
    if (!openPick || !reasonCode) return
    const snapshot = openPick
    setSending(true)
    let outboxId = 0
    try {
      outboxId = queueManualIssue(warehousePublicId!, reasonCode, issueNote, lines)
    } catch {
      setSending(false)
      setReasonError(t('errors.generic'))
      vibrateError()
      return
    }
    setDraft(null)
    setHint(null)
    setChosenClient(null)
    refresh()
    const result = await flushNow(outboxId)
    setSending(false)
    if (result.status === 'rejected') {
      // el servidor ya deshizo el efecto en los saldos locales (runOutbox); la fila no se deja en Sincronización: el despacho vuelve a la pantalla
      discardRow(outboxId)
      restoreLocalPick(snapshot)
      setPacking({ name: 'scan' })
      setNotice(null)
      setScanError(result.error || t('errors.generic'))
      vibrateError()
      refresh()
      return
    }
    // el próximo despacho abre con este motivo (si la compañía no lo deshabilita antes)
    saveLastReason(reasonCode)
    setPacking({ name: 'scan' })
    setReasonCode(null)
    setReasonSource(null)
    setIssueNote('')
    setShowNote(false)
    setScanError(null)
    if (result.status === 'sent') {
      const number = manualIssueNumber(outboxResult(outboxId))
      setNotice(number ? t('dispatch.issueSent', { number }) : t('dispatch.issueSentNoNumber'))
    } else {
      setNotice(t('dispatch.issueQueued'))
    }
    vibrateOk()
    refresh()
  }

  async function confirmPack(consignee: ConsigneeChoice) {
    if (!openPick) return
    setBusy(true)
    try {
      const resolved = await resolveBinCodes(warehousePublicId!, openPick.lineRows)
      if (resolved.notFound.length > 0) {
        setPacking({ name: 'error', message: t('putaway.binNotFound') + ' ' + resolved.notFound.join(', ') })
        return
      }
      const n = Math.max(1, Math.round(Number(pieces) || 1))
      await submitCollectAndPack(warehousePublicId!, (openPick.clientPublicId ?? chosenClient?.publicId)!, consignee.publicId, n, resolved.lines)
      setChosenClient(null)
      setClients(null)
      discardLocalPick()
      void runSync()
      router.replace('/home')
    } catch (err) {
      setPacking({ name: 'error', message: err instanceof ApiError ? err.title : t('errors.generic') })
    } finally {
      setBusy(false)
    }
  }

  // Despacho manual encolado: se espera al servidor unos segundos (el despacho local ya se cerró).
  if (sending) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('dispatch.title')}</Text>
        <ActivityIndicator color={colors.brand} />
        <Text style={styles.help}>{t('dispatch.issueSending')}</Text>
      </KeyboardScreen>
    )
  }

  // Sin despacho abierto o capturando líneas.
  if (!openPick || draft) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('dispatch.title')}</Text>
        {draft ? (
          <>
            <Text style={styles.help}>{draft.productName}</Text>
            <View style={styles.field}>
              <Text style={styles.label}>{t('dispatch.qtyLabel')}</Text>
              <KeyboardInput
                ref={qtyRef}
                autoFocus
                value={draft.qtyText}
                onChangeText={(v) => {
                  setBinError(null)
                  setDraft((d) => (d ? { ...d, qtyText: v } : d))
                }}
                keyboardType="decimal-pad"
                style={styles.input}
                accessibilityLabel={t('dispatch.qtyLabel')}
                testID="dispatch-qty"
              />
              <Text style={styles.help}>{t('dispatch.qtyFirstHelp')}</Text>
            </View>
            {suggestion ? (
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={t('positions.tapToUse')}
                onPress={tapSuggestion}
                style={styles.suggest}
                testID="dispatch-suggestion"
              >
                <Text style={styles.suggestTitle}>{t(hint?.lot ? 'dispatch.mustTakeFrom' : 'dispatch.suggestedFrom', { bin: suggestion.binCode })}</Text>
                <Text style={styles.help}>{stockDetail(suggestion)}</Text>
                {doesNotFit ? <Text style={styles.warnText}>{t('positions.doesNotFit', { short: f.qty(typedQty - suggestion.available) })}</Text> : null}
              </Pressable>
            ) : hint?.source === 'none' ? (
              <Text style={styles.help}>{t('dispatch.stockOffline')}</Text>
            ) : null}
            {suggestion && hint?.source === 'device' ? <Text style={styles.help}>{t('dispatch.stockFromDevice')}</Text> : null}
            {markOptions && markOptions.length > 1 && !doesNotFit ? (
              <BigButton label={t(showAll ? 'positions.hideAll' : 'positions.showAll')} variant="secondary" onPress={() => setShowAll((v) => !v)} />
            ) : null}
            {listVisible && markOptions ? (
              <BinMarkList
                testID="dispatch-plan"
                options={markOptions}
                marks={effectiveMarks}
                total={typedQty}
                onToggle={(key) => setMarks(toggleMark(markOptions, effectiveMarks, key, typedQty))}
                onMarkRecommended={() => setMarks(markRecommended(markOptions, effectiveMarks, typedQty))}
                onUse={addMarkedLines}
                useLabel={t('positions.useDispatch')}
              />
            ) : null}
            {(plan?.short ?? 0) > 0 ? <Text style={styles.error}>{t('dispatch.planShort', { short: f.qty(plan?.short ?? 0) })}</Text> : null}
            {/* sin botón "Agregar": la lectura de la posición (o Aceptar del campo) es la que agrega la línea */}
            <ScanField
              label={t('dispatch.fromBinLabel')}
              help={suggestion ? t(hint?.lot ? 'dispatch.fromBinHelpLot' : 'dispatch.fromBinHelpSuggested') : t('dispatch.fromBinHelp')}
              suggestedValue={suggestion?.binCode ?? null}
              prefill={prefill}
              autoFocus={false}
              error={binError}
              onSubmit={scanFromBin}
              pick="bin"
              testID="dispatch-from-bin"
            />
            <BigButton
              label={t('common.cancel')}
              variant="secondary"
              onPress={() => {
                setBinError(null)
                setDraft(null)
                setHint(null)
              }}
            />
          </>
        ) : (
          <>
            {/* Sin despacho abierto todavía (draft es null aquí): nada que perder, "Volver" sale directo a Inicio.
                2026-10-01 (Luis): abajo, pegado al borde inferior (`marginTop: auto`). */}
            <ScanField label={t('dispatch.scanProductLabel')} help={t('dispatch.scanProductHelp')} error={scanError} notice={notice} onSubmit={scanProduct} pick="product" />
            <View style={styles.bottom}>
              <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
            </View>
          </>
        )}
      </KeyboardScreen>
    )
  }

  // Despacho manual (2026-10-11 (b)): la confirmación con el motivo ya puesto; la lista de motivos solo si no hay ninguno o se toca «Cambiar».
  if (packing.name === 'reason') {
    const options = reasonOptions(readManualIssueReasons(), lang, t)
    const chosen = options.find((o) => o.code === reasonCode) ?? null
    const totals = issueTotals(packing.lines)
    const summary = (
      <Text style={styles.label}>
        {t('dispatch.issueSummary', { owner: openPick.clientName || t('dispatch.ownInventory'), lines: totals.lines, qty: f.qty(totals.qty) })}
      </Text>
    )
    if (pickingReason || !chosen) {
      return (
        <KeyboardScreen contentContainerStyle={styles.fill}>
          <Text style={styles.title}>{t('dispatch.reasonTitle')}</Text>
          <Text style={styles.help}>{t('dispatch.reasonHelp')}</Text>
          {summary}
          {options.length === 0 ? <Text style={styles.error}>{t('dispatch.noReasons')}</Text> : null}
          <View style={styles.reasons} accessibilityRole="radiogroup" accessibilityLabel={t('dispatch.reasonLabel')}>
            {options.map((o) => (
              <Pressable
                key={o.code}
                accessibilityRole="radio"
                accessibilityState={{ selected: reasonCode === o.code }}
                accessibilityLabel={o.label}
                onPress={() => pickReason(o.code)}
                style={[styles.reason, reasonCode === o.code && styles.reasonOn]}
                testID={`dispatch-reason-${o.code}`}
              >
                <Text style={styles.reasonText}>{o.label}</Text>
              </Pressable>
            ))}
          </View>
          {reasonError ? <Text style={styles.error}>{reasonError}</Text> : null}
          {/* «Volver» desde «Cambiar» regresa a la confirmación con el motivo que había; sin motivo, al despacho */}
          <BigButton label={t('common.back')} variant="secondary" onPress={() => (chosen ? setPickingReason(false) : setPacking({ name: 'scan' }))} />
        </KeyboardScreen>
      )
    }
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('dispatch.issueConfirmTitle')}</Text>
        <Text style={styles.help}>{t('dispatch.issueConfirmHelp')}</Text>
        {summary}
        <View style={styles.chosen}>
          <View style={styles.chosenText}>
            <Text style={styles.chosenLabel} testID="dispatch-issue-reason">
              {t('dispatch.issueReasonLine', { reason: chosen.label })}
            </Text>
            {reasonSource === 'last' || reasonSource === 'default' ? (
              <Text style={styles.help}>{t(reasonSource === 'last' ? 'dispatch.reasonFromLast' : 'dispatch.reasonFromDefault')}</Text>
            ) : null}
          </View>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={t('dispatch.changeReasonA11y')}
            onPress={() => {
              setReasonError(null)
              setPickingReason(true)
            }}
            style={({ pressed }) => [styles.smallBtn, pressed && styles.smallBtnPressed]}
            testID="dispatch-issue-change"
          >
            <Text style={styles.smallBtnText}>{t('dispatch.changeReason')}</Text>
          </Pressable>
        </View>
        {showNote ? (
          <View style={styles.field}>
            <Text style={styles.label}>{t('dispatch.noteLabel')}</Text>
            <KeyboardInput
              autoFocus
              value={issueNote}
              onChangeText={(v) => {
                setIssueNote(v)
                setReasonError(null)
              }}
              multiline
              maxLength={MANUAL_ISSUE_NOTE_MAX}
              style={[styles.input, styles.noteInput]}
              accessibilityLabel={t('dispatch.noteLabel')}
              testID="dispatch-issue-note"
            />
            <Text style={styles.help}>{t('dispatch.noteHelp', { count: issueNote.length, max: MANUAL_ISSUE_NOTE_MAX })}</Text>
          </View>
        ) : (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={t('dispatch.addNote')}
            onPress={() => setShowNote(true)}
            style={({ pressed }) => [styles.smallBtn, styles.smallBtnStart, pressed && styles.smallBtnPressed]}
            testID="dispatch-issue-add-note"
          >
            <Text style={styles.smallBtnText}>{t('dispatch.addNote')}</Text>
          </Pressable>
        )}
        {reasonError ? <Text style={styles.error}>{reasonError}</Text> : null}
        <BigButton label={t('dispatch.issueConfirmButton')} onPress={() => confirmManualIssue(packing.lines)} testID="dispatch-issue-confirm" />
        <BigButton label={t('common.back')} variant="secondary" onPress={() => setPacking({ name: 'scan' })} />
      </KeyboardScreen>
    )
  }

  // Inventario propio: elegir a qué cliente se despacha.
  if (packing.name === 'client') {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('dispatch.chooseClient')}</Text>
        {busy ? (
          <ActivityIndicator color={colors.brand} />
        ) : clients && clients.length === 0 ? (
          <Text style={styles.help}>{t('dispatch.noClients')}</Text>
        ) : (
          (clients ?? []).map((c) => <BigButton key={c.publicId} label={c.label} onPress={() => chooseClient(c)} />)
        )}
        <BigButton label={t('common.back')} variant="secondary" onPress={() => setPacking({ name: 'scan' })} disabled={busy} />
      </KeyboardScreen>
    )
  }

  // Empacando: resolviendo posiciones y eligiendo consignatario.
  if (packing.name === 'consignee' || packing.name === 'error') {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('dispatch.chooseConsignee')}</Text>
        <View style={styles.field}>
          <Text style={styles.label}>{t('dispatch.piecesLabel')}</Text>
          <KeyboardInput value={pieces} onChangeText={setPieces} keyboardType="number-pad" style={styles.input} accessibilityLabel={t('dispatch.piecesLabel')} />
        </View>
        {packing.name === 'error' ? (
          <Text style={styles.error}>{packing.message}</Text>
        ) : busy ? (
          <ActivityIndicator color={colors.brand} />
        ) : consignees && consignees.length === 0 ? (
          <Text style={styles.help}>{t('dispatch.noConsignees')}</Text>
        ) : (
          (consignees ?? []).map((c) => <BigButton key={c.publicId} label={c.label} onPress={() => confirmPack(c)} />)
        )}
        <BigButton label={t('common.back')} variant="secondary" onPress={() => setPacking(openPick.clientPublicId ? { name: 'scan' } : { name: 'client' })} disabled={busy} />
      </KeyboardScreen>
    )
  }

  // Despacho abierto: viendo lo recolectado. ScrollView (no View, mismo motivo que receive.tsx paso 2): el
  // ScanField se reenfoca tras cada línea y, con el teclado abierto, tapaba Empacar/Cancelar.
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{openPick.clientName || t('dispatch.ownInventory')}</Text>
      <ScanField label={t('dispatch.scanProductLabel')} help={t('dispatch.scanProductHelp')} error={scanError} notice={notice} onSubmit={scanProduct} pick="product" />
      <Text style={styles.label}>{t('dispatch.linesTitle')}</Text>
      <LineList
        items={openPick.lineRows.map((l) => ({ id: l.id, title: t('dispatch.lineQty', { qty: l.quantity, sku: l.sku }), subtitle: l.fromBinCode }))}
        onRemove={(id) => {
          const line = openPick.lineRows.find((r) => r.id === Number(id))
          Alert.alert(t('dispatch.removeLineTitle'), t('dispatch.removeLineBody', { qty: line?.quantity ?? '', sku: line?.sku ?? '', bin: line?.fromBinCode ?? '' }), [
            { text: t('common.no'), style: 'cancel' },
            {
              text: t('common.remove'),
              style: 'destructive',
              onPress: () => {
                removeLocalPickLine(Number(id))
                refresh()
              },
            },
          ])
        }}
        removeLabel={t('common.remove')}
        emptyLabel={t('dispatch.linesTitle')}
      />
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      {/* 2026-10-11: «Completar despacho» = despacho manual, solo con warehouse.issue; sin él queda «Empacar» y el aviso */}
      {canIssue ? (
        <>
          <BigButton label={t('dispatch.completeButton')} onPress={() => void startManualIssue()} disabled={openPick.lineRows.length === 0 || busy} testID="dispatch-complete" />
          <Text style={styles.help}>{t('dispatch.completeHelp')}</Text>
        </>
      ) : (
        <Text style={styles.warnText} testID="dispatch-no-issue">
          {t(permissions === null ? 'dispatch.issueUnknownPermission' : 'dispatch.issueNoPermission')}
        </Text>
      )}
      <BigButton label={t('dispatch.packButton')} variant="secondary" onPress={startPacking} disabled={openPick.lineRows.length === 0 || busy} />
      <Text style={styles.help}>{t('dispatch.packHelp')}</Text>
      <BigButton label={t('dispatch.cancelDispatch')} variant="danger" onPress={cancelDispatch} />
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
  // Pega el botón al borde inferior cuando el contenido es corto (el contenedor del ScrollView crece: flexGrow 1).
  bottom: { marginTop: 'auto' },
  // la posición sugerida (o la obligatoria, con lote): grande y a la vista, encima del campo de la posición
  suggest: { gap: 2, padding: spacing.md, borderRadius: 12, borderWidth: 2, borderColor: colors.brand, backgroundColor: colors.panelAlt },
  warnText: { color: colors.warn, fontSize: fontSize.message, fontWeight: '700' },
  suggestTitle: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  planRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm },
  // motivos del despacho manual: botones grandes, uno por renglón (mismo estilo que los motivos de Ajustar y el destino de Daño)
  reasons: { gap: spacing.sm },
  reason: { minHeight: 56, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  reasonOn: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  reasonText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  noteInput: { minHeight: 88, paddingVertical: spacing.sm, textAlignVertical: 'top' },
  // confirmación del despacho manual: el motivo puesto, grande, con «Cambiar» al lado (pasa abajo si no cabe a 360 px)
  chosen: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.brand,
    backgroundColor: colors.panelAlt,
  },
  chosenText: { flexGrow: 1, flexShrink: 1, flexBasis: 180, gap: 2 },
  chosenLabel: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  // botón chico (Cambiar, Agregar nota): menos llamativo que los grandes, con área táctil cómoda
  smallBtn: { minHeight: 48, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.sm, borderWidth: 1, borderColor: colors.line },
  smallBtnStart: { alignSelf: 'flex-start' },
  smallBtnPressed: { backgroundColor: colors.panel },
  smallBtnText: { color: colors.text, fontSize: fontSize.message, fontWeight: '600' },
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
