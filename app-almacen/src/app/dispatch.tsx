import { useMemo, useRef, useState } from 'react'
import { ActivityIndicator, Alert, StyleSheet, Text, TextInput, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError } from '../kernel/api/client'
import { useFormat } from '../kernel/format/useFormat'
import { useSession } from '../kernel/auth/useSession'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useT } from '../kernel/i18n/useT'
import { runSync } from '../kernel/sync/engine'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { fetchClientsForOwnDispatch, fetchConsigneesForClient, fetchStockOptions, resolveBinCodes, submitCollectOnly, submitCollectAndPack } from '../features/dispatch/dispatchApi'
import { addLocalPickLine, discardLocalPick, getOpenPick, removeLocalPickLine, startLocalPick } from '../features/dispatch/localPick'
import {
  binScanOutcome,
  checkLotBin,
  nextStockOption,
  pickedQty,
  pickQtyState,
  type StockOption,
  type ClientChoice,
  type ConsigneeChoice,
  newPickLineDraft,
  sameOwner,
  type PickLine,
  type PickLineDraft,
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

type Step = { name: 'scan' } | { name: 'client' } | { name: 'consignee' } | { name: 'error'; message: string }

/** Pantalla 5 (docs/mobile/app-almacen-plan.md §2): recolectar (producto, cantidad, posición) sin señal; empacar
 *  (elegir consignatario y confirmar) necesita señal un momento y luego se manda por la cola. Solo clientes 3PL por
 *  ahora (docs/lote8A-app-decisiones.md): el dueño del producto ya viene sincronizado. Inventario propio (2026-10-05): se
 *  despacha igual; al empacar se elige primero el cliente a quien se despacha y luego su consignatario. */
export default function DispatchScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null
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

  // tick fuerza releer la base local tras cada mutación; getOpenPick() no usa tick.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const openPick = useMemo(() => getOpenPick(), [tick])
  const refresh = () => setTick((n) => n + 1)

  // La sugerencia: el primer lugar de salida con disponible tras descontar lo que este despacho ya sacó de ese producto.
  const suggestion =
    draft && hint && hint.productPublicId === draft.productPublicId && hint.options
      ? nextStockOption(hint.options, pickedQty(openPick?.lineRows ?? [], draft.productPublicId))
      : null
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

  /** Completar el despacho SIN empacar (pedido del dueño 2026-10-05): el inventario sale (recolección) sin orden ni empaque; empacar es opcional. */
  async function completeDispatch() {
    if (!openPick || openPick.lineRows.length === 0) return
    setBusy(true)
    try {
      const resolved = await resolveBinCodes(warehousePublicId!, openPick.lineRows)
      if (resolved.notFound.length > 0) {
        setScanError(t('putaway.binNotFound') + ' ' + resolved.notFound.join(', '))
        vibrateError()
        return
      }
      await submitCollectOnly(warehousePublicId!, resolved.lines)
      discardLocalPick()
      void runSync()
      vibrateOk()
      router.replace('/home')
    } catch (err) {
      setScanError(err instanceof ApiError ? err.title : t('errors.generic'))
      vibrateError()
    } finally {
      setBusy(false)
    }
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
              <View style={styles.suggest} testID="dispatch-suggestion">
                <Text style={styles.suggestTitle}>{t(hint?.lot ? 'dispatch.mustTakeFrom' : 'dispatch.suggestedFrom', { bin: suggestion.binCode })}</Text>
                <Text style={styles.help}>{stockDetail(suggestion)}</Text>
              </View>
            ) : hint?.source === 'none' ? (
              <Text style={styles.help}>{t('dispatch.stockOffline')}</Text>
            ) : null}
            {suggestion && hint?.source === 'device' ? <Text style={styles.help}>{t('dispatch.stockFromDevice')}</Text> : null}
            {/* sin botón "Agregar": la lectura de la posición (o Aceptar del campo) es la que agrega la línea */}
            <ScanField
              label={t('dispatch.fromBinLabel')}
              help={suggestion ? t(hint?.lot ? 'dispatch.fromBinHelpLot' : 'dispatch.fromBinHelpSuggested') : t('dispatch.fromBinHelp')}
              suggestedValue={suggestion?.binCode ?? null}
              autoFocus={false}
              error={binError}
              onSubmit={scanFromBin}
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
            <ScanField label={t('dispatch.scanProductLabel')} help={t('dispatch.scanProductHelp')} error={scanError} notice={notice} onSubmit={scanProduct} />
            <View style={styles.bottom}>
              <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
            </View>
          </>
        )}
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
      <ScanField label={t('dispatch.scanProductLabel')} help={t('dispatch.scanProductHelp')} error={scanError} notice={notice} onSubmit={scanProduct} />
      <Text style={styles.label}>{t('dispatch.linesTitle')}</Text>
      <LineList
        items={openPick.lineRows.map((l) => ({ id: l.id, title: t('dispatch.lineQty', { qty: l.quantity, sku: l.sku }), subtitle: l.fromBinCode }))}
        onRemove={(id) => {
          removeLocalPickLine(Number(id))
          refresh()
        }}
        removeLabel={t('common.remove')}
        emptyLabel={t('dispatch.linesTitle')}
      />
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      <BigButton label={t('dispatch.completeButton')} onPress={() => void completeDispatch()} disabled={openPick.lineRows.length === 0 || busy} />
      <Text style={styles.help}>{t('dispatch.completeHelp')}</Text>
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
  suggestTitle: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
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
