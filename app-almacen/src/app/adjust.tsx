import { useEffect, useState } from 'react'
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, View } from 'react-native'
import { useLocalSearchParams, useRouter } from 'expo-router'

import { canSeeSystemQty, useMyPermissions } from '../kernel/auth/permissions'
import { ApiError, isNetworkError } from '../kernel/api/client'
import { useT } from '../kernel/i18n/useT'
import { useFormat } from '../kernel/format/useFormat'
import { findProductByCode, findProductPack } from '../kernel/warehouse/productLookup'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { BigButton } from '../kernel/ui/BigButton'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { ScanMessage } from '../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { QuantityField } from '../features/count/QuantityField'
import { parseQty } from '../features/count/countLogic'
import { fetchBinContents, resolveLookupBin, searchBalances } from '../features/lookup/lookupApi'
import { localBinRows } from '../features/lookup/lookupFlow'
import { RefreshNote, type RefreshState } from '../kernel/ui/RefreshNote'
import type { BalanceRow } from '../features/lookup/lookupLogic'
import { isBlockedZone } from '../features/transfer/transferLogic'
import { flushNow } from '../kernel/sync/engine'
import { queueAdjustment } from '../features/adjust/adjustApi'
import { adjustIssue, afterQty, NOTE_MAX, reasonsFor, type AdjustDirection } from '../features/adjust/adjustLogic'

/**
 * 2026-10-10 — Ajustar: cambia SOLO la cantidad de una posición (sube o baja). Mover de una posición a otra es Transferir («Mover» en Consultar).
 * Con el permiso warehouse.adjust se llega de dos maneras:
 *  - desde Consultar con «Ajustar» (llega con la posición y el producto ya puestos);
 *  - 2026-10-11, desde el menú principal «Ajustar»: se escanea la posición y el producto. Sirve para un producto que el sistema da en 0 (o que nunca estuvo)
 *    en esa posición y que se ve ahí: Consultar no lo lista, y este camino deja SUBIRLO (motivo «Encontrado»). Un producto con lote sin saldo en la posición
 *    ofrece los lotes que ya tiene registrados; sin ninguno, se ajusta desde la web.
 * Pide dirección, cantidad, motivo y nota (obligatoria). Desde 2026-10-11 el ajuste se guarda en el aparato y se manda solo (cola de salida, D2b).
 */
export default function AdjustScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const params = useLocalSearchParams<{ fromBinId?: string; fromBinCode?: string; productPublicId?: string }>()
  const warehousePublicId = useActiveWarehouse().publicId
  const permissions = useMyPermissions()
  const showSystemQty = canSeeSystemQty(permissions)
  // la posición y el producto llegan de Consultar (params) o se escanean aquí (menú principal)
  const fromLookup = Boolean(params.fromBinId && params.fromBinCode && params.productPublicId)
  const [bin, setBin] = useState<{ id: number; code: string } | null>(params.fromBinId && params.fromBinCode ? { id: Number(params.fromBinId), code: params.fromBinCode } : null)
  const [prod, setProd] = useState<{ publicId: string; sku: string; name: string } | null>(params.productPublicId ? { publicId: params.productPublicId, sku: '', name: '' } : null)
  const [extraLots, setExtraLots] = useState<{ lotId: number; lotNumber: string }[] | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [rows, setRows] = useState<BalanceRow[]>([])
  const [loaded, setLoaded] = useState(false)
  const [lotId, setLotId] = useState<number | null>(null)
  const [direction, setDirection] = useState<AdjustDirection | null>(null)
  const [qtyText, setQtyText] = useState('')
  const [reason, setReason] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [refresh, setRefresh] = useState<RefreshState>('idle')

  const binId = bin?.id ?? 0
  const binCode = bin?.code ?? ''
  const productPublicId = prod?.publicId ?? ''
  // el producto en la base local (seguimiento: sin serie ni lote ajustables desde aquí): por el SKU de las filas o, si la posición no tiene saldo de él, el escaneado
  const sku = rows[0]?.sku || prod?.sku || ''
  const local = sku ? findProductByCode(sku) : null
  const tracking = local?.trackingTypeCode ?? 'NONE'
  const serial = tracking === 'SERIAL'

  function fail(message: string) {
    setError(message)
    vibrateError()
  }
  function failure(err: unknown) {
    fail(isNetworkError(err) ? t('adjust.needSignal') : err instanceof ApiError ? err.title : t('errors.generic'))
  }

  // Señal débil: primero lo que el aparato ya sabe de la posición (al instante); el servidor lo pone al día en segundo plano.
  useEffect(() => {
    if (!warehousePublicId || !binId || !productPublicId) return
    let alive = true
    function show(all: BalanceRow[], first: boolean): boolean {
      if (all.some((r) => isBlockedZone(r.zoneTypeCode))) {
        fail(t('adjust.zoneBlocked', { bin: binCode }))
        return false
      }
      const mine = all.filter((r) => r.productPublicId === productPublicId)
      setRows(mine)
      if (first) {
        const withLot = mine.filter((r) => r.lotId != null)
        setLotId(withLot.length === 1 ? (withLot[0].lotId ?? null) : null)
      }
      setLoaded(true)
      return true
    }
    const localRows = localBinRows(warehousePublicId, binId)
    const shownLocal = localRows != null && show(localRows, true)
    if (localRows != null && !shownLocal) return
    if (shownLocal) setRefresh('syncing')
    else setBusy(true)
    fetchBinContents(warehousePublicId, { id: binId, code: binCode })
      .then((found) => {
        if (!alive) return
        if (found.fromCache && shownLocal) return setRefresh('offline')
        show(found.rows, !shownLocal)
        setRefresh('done')
      })
      .catch((err) => {
        if (!alive) return
        if (shownLocal) setRefresh('offline')
        else failure(err)
      })
      .finally(() => alive && setBusy(false))
    return () => {
      alive = false
    }
    // al abrir la pantalla con la posición y el producto, o al quedar elegidos los dos
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [binId, productPublicId])

  // Producto con lote que NO tiene saldo en esta posición: se ofrecen los lotes que ya tiene registrados en el almacén (el API ajusta por id de lote).
  useEffect(() => {
    if (!loaded || !warehousePublicId || !prod || tracking !== 'LOT' || extraLots !== null) return
    if (rows.some((r) => r.lotId != null)) {
      setExtraLots([])
      return
    }
    let alive = true
    searchBalances(warehousePublicId, sku, prod.publicId)
      .then((res) => {
        if (!alive) return
        const seen = new Map<number, string>()
        for (const r of res.rows) if (r.lotId != null && !seen.has(r.lotId)) seen.set(r.lotId, r.lotNumber ?? String(r.lotId))
        const list = [...seen].map(([lotId, lotNumber]) => ({ lotId, lotNumber }))
        setExtraLots(list)
        if (list.length === 1) setLotId(list[0].lotId)
      })
      .catch((err) => alive && failure(err))
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loaded, tracking, productPublicId])

  async function scanBin(code: string) {
    setError(null)
    setNotice(null)
    setBusy(true)
    try {
      const match = await resolveLookupBin(warehousePublicId!, code)
      if (match.kind === 'none') return fail(t('adjust.binNotFound'))
      if (match.kind === 'inactive') return fail(t('adjust.binInactive', { bin: match.code }))
      setBin({ id: match.bin.id, code: match.bin.code })
      vibrateOk()
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  function scanProduct(code: string) {
    setError(null)
    const found = findProductByCode(code.trim())
    if (!found) return fail(t('adjust.productNotFound'))
    if (found.trackingTypeCode === 'SERIAL') return fail(t('adjust.serialNotSupported', { sku: found.sku }))
    setProd({ publicId: found.publicId, sku: found.sku, name: found.name })
    vibrateOk()
  }

  /** Vuelve al primer paso (posición): lo escaneado, lo elegido y lo escrito se limpian. */
  function restart() {
    setBin(null)
    setProd(null)
    setRows([])
    setLoaded(false)
    setLotId(null)
    setExtraLots(null)
    setDirection(null)
    setQtyText('')
    setReason(null)
    setNote('')
    setError(null)
    setRefresh('idle')
  }

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
        <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace(fromLookup ? '/lookup' : '/home')} />
      </View>
    )
  }

  const first = rows[0]
  const lotRows = rows.filter((r) => r.lotId != null)
  // lotes entre los que se elige: los de la posición o, si el producto con lote no tiene saldo aquí, los que ya tiene registrados
  const lotOptions = lotRows.length > 0
    ? lotRows.map((r) => ({ lotId: r.lotId as number, lotNumber: r.lotNumber ?? String(r.lotId), onHand: r.qtyOnHand }))
    : tracking === 'LOT'
      ? (extraLots ?? []).map((l) => ({ ...l, onHand: 0 }))
      : []
  const needLot = lotOptions.length > 1 && lotId == null
  const lotMissing = tracking === 'LOT' && loaded && lotRows.length === 0 && extraLots !== null && extraLots.length === 0
  const waitingLots = tracking === 'LOT' && loaded && lotRows.length === 0 && extraLots === null
  // el saldo de esta posición (y lote): sin fila es 0, que es justo el caso de un producto que se ve y el sistema no tiene aquí
  const row = lotRows.length > 0 ? lotRows.find((r) => r.lotId === lotId) : rows.find((r) => r.lotId == null) ?? first
  const onHand = row?.qtyOnHand ?? 0
  const available = row?.qtyAvailable ?? 0
  const ready = loaded && !serial && !lotMissing && !waitingLots && !needLot && (lotOptions.length === 0 || lotId != null)
  const pack = sku ? findProductPack({ publicId: productPublicId, sku }) : null
  const issue = adjustIssue({ direction, qtyText, reason, note, available })
  const qty = parseQty(qtyText)

  function chooseDirection(d: AdjustDirection) {
    setDirection(d)
    if (reason && !reasonsFor(d).some((r) => r.code === reason)) setReason(null)
  }

  function ask() {
    if (issue || direction == null || qty === null || reason == null) {
      fail(t(`adjust.issue${(issue ?? 'qty').charAt(0).toUpperCase()}${(issue ?? 'qty').slice(1)}`, { qty: f.qty(available), max: NOTE_MAX }))
      return
    }
    setError(null)
    Alert.alert(
      t('adjust.confirmTitle'),
      showSystemQty
        ? t('adjust.confirmBodyQty', { dir: t(`adjust.${direction}`), qty: f.qty(qty), sku, bin: binCode, from: f.qty(onHand), to: f.qty(afterQty(onHand, direction, qty)) })
        : t('adjust.confirmBody', { dir: t(`adjust.${direction}`), qty: f.qty(qty), sku, bin: binCode }),
      [
        { text: t('common.no'), style: 'cancel' },
        { text: t('adjust.doIt'), onPress: () => void submit(direction, qty, reason) },
      ],
    )
  }

  async function submit(dir: AdjustDirection, q: number, why: string) {
    setError(null)
    // D2b: se guarda en el aparato (el saldo local ya refleja el ajuste) y se manda solo; se vuelve a Consultar sin esperar al servidor
    const outboxId = queueAdjustment({ warehousePublicId: warehousePublicId!, productPublicId, binId, lotId: lotOptions.length > 0 ? lotId : null, direction: dir, quantity: q, reason: why, note })
    vibrateOk()
    if (fromLookup) router.replace('/lookup')
    else {
      // desde el menú: se queda en Ajustar, listo para la siguiente posición
      restart()
      setNotice(t('adjust.done', { dir: t(`adjust.${dir}`), qty: f.qty(q), sku, bin: binCode }))
    }
    const result = await flushNow(outboxId)
    if (result.status === 'rejected') Alert.alert(t('common.rejectedTitle'), t('common.rejectedBody', { error: result.error }))
  }

  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('adjust.title')}</Text>
      {notice ? <ScanMessage message={notice} tone="ok" /> : null}
      {bin ? <Text style={styles.summary}>{t('adjust.inBin', { bin: binCode })}</Text> : null}
      {prod && sku ? <Text style={styles.product}>{`${sku} · ${first?.productName || prod.name}`}</Text> : null}
      {!bin ? (
        <ScanField key="bin" label={t('adjust.scanBin')} help={t('adjust.scanBinHelp')} error={error} onSubmit={(c) => void scanBin(c)} pick="bin" testID="adjust-bin" />
      ) : null}
      {bin && !prod ? <ScanField key="product" label={t('adjust.scanProduct')} error={error} onSubmit={scanProduct} pick="product" testID="adjust-product" /> : null}
      {prod ? <ScanMessage message={error} tone="error" /> : null}
      {busy && !loaded ? <ActivityIndicator color={colors.brand} /> : null}
      <RefreshNote state={refresh} />

      {serial ? <Text style={styles.error}>{t('adjust.serialNotSupported', { sku })}</Text> : null}
      {lotMissing ? <Text style={styles.error}>{t('adjust.lotNone', { sku })}</Text> : null}

      {loaded && !serial && !lotMissing && needLot ? (
        <>
          <Text style={styles.label}>{t('adjust.chooseLot')}</Text>
          <LineList
            items={lotOptions.map((l) => ({ id: l.lotId, title: l.lotNumber, subtitle: showSystemQty ? t('adjust.onHand', { qty: f.qty(l.onHand) }) : undefined }))}
            removeLabel={t('common.remove')}
            onPressItem={(id) => setLotId(Number(id))}
            pressLabel={(item) => t('adjust.useLot', { lot: item.title })}
          />
        </>
      ) : null}

      {ready ? (
        <>
          {showSystemQty ? <Text style={styles.help}>{t('adjust.systemQty', { onHand: f.qty(onHand), available: f.qty(available) })}</Text> : null}
          <View style={styles.dirRow}>
            {(['up', 'down'] as const).map((d) => (
              <Pressable
                key={d}
                accessibilityRole="radio"
                accessibilityState={{ selected: direction === d }}
                accessibilityLabel={t(`adjust.${d}Label`)}
                onPress={() => chooseDirection(d)}
                style={[styles.dir, direction === d && styles.dirOn]}
                testID={`adjust-${d}`}
              >
                <Text style={styles.dirText}>{t(`adjust.${d}Label`)}</Text>
              </Pressable>
            ))}
          </View>

          {direction ? (
            <>
              <Text style={styles.label}>{t('adjust.qtyLabel')}</Text>
              {direction === 'down' ? <Text style={styles.help}>{t('adjust.canLower', { qty: f.qty(available) })}</Text> : null}
              <QuantityField value={qtyText} onChangeText={setQtyText} style={styles.input} accessibilityLabel={t('adjust.qtyLabel')} pack={pack} />
              <Text style={styles.label}>{t('adjust.reasonLabel')}</Text>
              <View style={styles.reasons}>
                {reasonsFor(direction).map((r) => (
                  <Pressable
                    key={r.code}
                    accessibilityRole="radio"
                    accessibilityState={{ selected: reason === r.code }}
                    onPress={() => setReason(r.code)}
                    style={[styles.reason, reason === r.code && styles.reasonOn]}
                    testID={`adjust-reason-${r.code}`}
                  >
                    <Text style={styles.reasonText}>{t(r.key)}</Text>
                  </Pressable>
                ))}
              </View>
              <Text style={styles.label}>{t('adjust.noteLabel')}</Text>
              <KeyboardInput value={note} onChangeText={setNote} style={styles.input} accessibilityLabel={t('adjust.noteLabel')} maxLength={NOTE_MAX} testID="adjust-note" />
              <Text style={styles.help}>{t('adjust.noteHelp')}</Text>
              <BigButton label={t('adjust.doIt')} onPress={ask} disabled={busy} testID="adjust-confirm" />
            </>
          ) : null}
        </>
      ) : null}

      <View style={styles.bottom}>
        {bin && !fromLookup ? <BigButton label={t('adjust.restart')} variant="secondary" onPress={restart} disabled={busy} testID="adjust-restart" /> : null}
        <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace(fromLookup ? '/lookup' : '/home')} disabled={busy} />
      </View>
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  summary: { color: colors.muted, fontSize: fontSize.label, fontWeight: '600' },
  product: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  bottom: { marginTop: 'auto', gap: spacing.sm },
  input: { minHeight: 56, borderWidth: 2, borderColor: colors.line, borderRadius: 12, paddingHorizontal: spacing.md, fontSize: 20, color: colors.text, backgroundColor: colors.panelAlt },
  dirRow: { flexDirection: 'row', gap: spacing.sm },
  dir: { flex: 1, minHeight: 64, alignItems: 'center', justifyContent: 'center', borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  dirOn: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  dirText: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  reasons: { gap: spacing.sm },
  reason: { minHeight: 56, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  reasonOn: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  reasonText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
})
