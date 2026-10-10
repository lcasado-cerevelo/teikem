import { useEffect, useRef, useState } from 'react'
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native'
import { useLocalSearchParams, useRouter } from 'expo-router'

import { ApiError, isNetworkError } from '../kernel/api/client'
import { useT } from '../kernel/i18n/useT'
import { useFormat } from '../kernel/format/useFormat'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { findProductPack } from '../kernel/warehouse/productLookup'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { BigButton } from '../kernel/ui/BigButton'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { ScanMessage } from '../kernel/ui/ScanMessage'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import type { BalanceRow } from '../features/lookup/lookupLogic'
import { QuantityField } from '../features/count/QuantityField'
import { parseQty } from '../features/count/countLogic'
import { fetchBinContents, resolveLookupBin } from '../features/lookup/lookupApi'
import { localBinRows } from '../features/lookup/lookupFlow'
import { RefreshNote, type RefreshState } from '../kernel/ui/RefreshNote'
import { transferInWarehouse } from '../features/transfer/transferApi'
import {
  isBlockedZone,
  lotChoices,
  movableOf,
  productsToMove,
  qtyIssue,
  sameBin,
  type TransferBin,
  type TransferProduct,
} from '../features/transfer/transferLogic'

/**
 * 2026-10-10 — Transferir: mueve inventario de una posición a otra del MISMO almacén (permiso warehouse.transfer). Pasos: posición de origen → producto
 * (se escanea o se toca de la lista de lo que hay ahí) → lote (si el producto lo trae) → cantidad → posición destino → confirmar. También se abre desde
 * Consultar con «Mover» (llega con la posición y el producto ya puestos). Necesita señal: mueve inventario en el servidor, sin cola (como Daño).
 */
export default function TransferScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const params = useLocalSearchParams<{ fromBinId?: string; fromBinCode?: string; productPublicId?: string }>()
  const warehousePublicId = useActiveWarehouse().publicId
  const [from, setFrom] = useState<TransferBin | null>(null)
  const [products, setProducts] = useState<TransferProduct[]>([])
  const [product, setProduct] = useState<TransferProduct | null>(null)
  const [lotId, setLotId] = useState<number | null>(null)
  const [qtyText, setQtyText] = useState('')
  const [qtyDone, setQtyDone] = useState(false)
  const [to, setTo] = useState<TransferBin | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [refresh, setRefresh] = useState<RefreshState>('idle')
  // número de la posición de origen que se está leyendo: la respuesta tardía de una anterior no pisa la actual
  const originSeq = useRef(0)

  function fail(message: string) {
    setError(message)
    vibrateError()
  }
  function failure(err: unknown) {
    fail(isNetworkError(err) ? t('transfer.needSignal') : err instanceof ApiError ? err.title : t('errors.generic'))
  }

  function reset() {
    originSeq.current += 1
    setRefresh('idle')
    setFrom(null)
    setProducts([])
    setProduct(null)
    setLotId(null)
    setQtyText('')
    setQtyDone(false)
    setTo(null)
    setError(null)
  }

  /** Valida lo que hay en la posición de origen y deja listos los productos movibles. false = no se puede (ya avisó el motivo). */
  function showOrigin(rows: BalanceRow[], bin: TransferBin, preselect?: string | null): boolean {
    if (rows.some((r) => isBlockedZone(r.zoneTypeCode))) {
      fail(t('transfer.zoneBlocked', { bin: bin.code }))
      return false
    }
    const list = productsToMove(rows)
    if (list.length === 0) {
      fail(t('transfer.nothingToMove', { bin: bin.code }))
      return false
    }
    setFrom(bin)
    setProducts(list)
    const pre = preselect ? list.find((p) => p.productPublicId === preselect) : null
    if (pre) chooseProduct(pre)
    return true
  }

  /** Lee lo que hay en la posición de origen. Señal débil: primero lo que el aparato ya sabe (al instante) y el servidor lo pone al día en segundo plano. */
  async function loadOrigin(bin: TransferBin, preselect?: string | null) {
    const seq = ++originSeq.current
    setError(null)
    const localRows = localBinRows(warehousePublicId!, bin.id)
    if (localRows) {
      if (!showOrigin(localRows, bin, preselect)) return
      vibrateOk()
      setRefresh('syncing')
      try {
        const found = await fetchBinContents(warehousePublicId!, bin)
        if (seq !== originSeq.current) return
        if (found.fromCache) return setRefresh('offline')
        const list = productsToMove(found.rows)
        if (found.rows.some((r) => isBlockedZone(r.zoneTypeCode)) || list.length === 0) {
          // el servidor manda: lo local estaba desactualizado y ya no se puede mover de aquí
          reset()
          fail(found.rows.some((r) => isBlockedZone(r.zoneTypeCode)) ? t('transfer.zoneBlocked', { bin: bin.code }) : t('transfer.nothingToMove', { bin: bin.code }))
          return setRefresh('done')
        }
        setProducts(list)
        setProduct((cur) => (cur ? (list.find((p) => (p.productPublicId || p.sku) === (cur.productPublicId || cur.sku)) ?? cur) : cur))
        setRefresh('done')
      } catch {
        if (seq === originSeq.current) setRefresh('offline')
      }
      return
    }
    setBusy(true)
    try {
      const found = await fetchBinContents(warehousePublicId!, bin)
      if (seq !== originSeq.current) return
      if (showOrigin(found.rows, bin, preselect)) vibrateOk()
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  // Desde Consultar («Mover»): la posición y, si se tocó un producto, el producto llegan ya puestos.
  useEffect(() => {
    if (!warehousePublicId || !params.fromBinId || !params.fromBinCode) return
    void loadOrigin({ id: Number(params.fromBinId), code: params.fromBinCode }, params.productPublicId ?? null)
    // solo al abrir la pantalla
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  async function scanBin(code: string, role: 'from' | 'to') {
    setError(null)
    setBusy(true)
    try {
      const match = await resolveLookupBin(warehousePublicId!, code)
      if (match.kind === 'none') return fail(t('transfer.binNotFound'))
      if (match.kind === 'inactive') return fail(t('transfer.binInactive', { bin: match.code }))
      if (role === 'from') {
        await loadOrigin({ id: match.bin.id, code: match.bin.code })
        return
      }
      if (sameBin(from, match.bin)) return fail(t('transfer.sameBin'))
      setTo({ id: match.bin.id, code: match.bin.code })
      vibrateOk()
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  function chooseProduct(p: TransferProduct) {
    setError(null)
    const local = findProductByCode(p.sku)
    if (local?.trackingTypeCode === 'SERIAL') return fail(t('transfer.serialNotSupported', { sku: p.sku }))
    setProduct(p)
    const lots = lotChoices(p)
    setLotId(lots.length === 1 ? (lots[0].lotId ?? null) : null)
    setQtyText('')
    setQtyDone(false)
    setTo(null)
  }

  function scanProduct(code: string) {
    setError(null)
    const local = findProductByCode(code)
    const hit = local ? products.find((p) => p.productPublicId === local.publicId) : products.find((p) => p.sku.toUpperCase() === code.trim().toUpperCase())
    if (!hit) return fail(t('transfer.productNotHere', { bin: from?.code ?? '' }))
    chooseProduct(hit)
    vibrateOk()
  }

  const lots = product ? lotChoices(product) : []
  const needLot = lots.length > 0 && lotId == null
  const movable = product ? movableOf(product, lots.length > 0 ? lotId : null) : 0
  const issue = product && !needLot ? qtyIssue(qtyText, movable) : null
  const qty = parseQty(qtyText)
  const pack = product ? findProductPack({ publicId: product.productPublicId, sku: product.sku }) : null
  const step = !from ? 'from' : !product ? 'product' : needLot ? 'lot' : !qtyDone ? 'qty' : !to ? 'to' : 'confirm'

  async function confirm() {
    if (!from || !to || !product || qty === null) return
    setBusy(true)
    setError(null)
    try {
      await transferInWarehouse({ warehousePublicId: warehousePublicId!, productPublicId: product.productPublicId, from, to, quantity: qty, lotId: lots.length > 0 ? lotId : null })
      setNotice(t('transfer.done', { qty: f.qty(qty), sku: product.sku, from: from.code, to: to.code }))
      reset()
      vibrateOk()
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('transfer.title')}</Text>
      <ScanMessage message={notice} tone="ok" />

      {from ? <Text style={styles.summary}>{t('transfer.fromBin', { bin: from.code })}</Text> : null}
      <RefreshNote state={refresh} />
      {product ? <Text style={styles.product}>{`${product.sku} · ${product.productName}`}</Text> : null}
      {to ? <Text style={styles.summary}>{t('transfer.toBin', { bin: to.code })}</Text> : null}

      {step === 'from' ? (
        <ScanField key="from" label={t('transfer.scanFrom')} help={t('transfer.scanFromHelp')} error={error} onSubmit={(c) => void scanBin(c, 'from')} pick="bin" testID="transfer-from" />
      ) : null}

      {step === 'product' ? (
        <>
          <ScanField key="product" label={t('transfer.scanProduct')} error={error} onSubmit={scanProduct} pick="product" testID="transfer-product" />
          <Text style={styles.help}>{t('transfer.pickHint')}</Text>
          <LineList
            items={products.map((p) => ({ id: p.productPublicId || p.sku, title: p.productName || p.sku, subtitle: `${p.sku} · ${t('transfer.movable', { qty: f.qty(p.available) })}` }))}
            removeLabel={t('common.remove')}
            onPressItem={(id) => {
              const p = products.find((x) => (x.productPublicId || x.sku) === id)
              if (p) chooseProduct(p)
            }}
            pressLabel={(item) => t('transfer.useProduct', { sku: item.subtitle?.split(' · ')[0] ?? item.title })}
          />
        </>
      ) : null}

      {step === 'lot' ? (
        <>
          <Text style={styles.label}>{t('transfer.chooseLot')}</Text>
          <LineList
            items={lots.map((r) => ({ id: r.lotId ?? 0, title: r.lotNumber ?? String(r.lotId), subtitle: t('transfer.movable', { qty: f.qty(r.qtyAvailable) }) }))}
            removeLabel={t('common.remove')}
            onPressItem={(id) => setLotId(Number(id))}
            pressLabel={(item) => t('transfer.useLot', { lot: item.title })}
          />
        </>
      ) : null}

      {step === 'qty' ? (
        <View style={styles.field}>
          <Text style={styles.label}>{t('transfer.qtyLabel')}</Text>
          <Text style={styles.help}>{t('transfer.movable', { qty: f.qty(movable) })}</Text>
          <QuantityField value={qtyText} onChangeText={setQtyText} style={styles.input} accessibilityLabel={t('transfer.qtyLabel')} autoFocus pack={pack} />
          {issue === 'tooMany' ? <Text style={styles.error}>{t('transfer.tooMany', { qty: f.qty(movable) })}</Text> : null}
          {issue === 'invalid' && qtyText.trim() !== '' ? <Text style={styles.error}>{t('transfer.qtyInvalid')}</Text> : null}
        </View>
      ) : null}

      {step === 'to' ? (
        <ScanField key="to" label={t('transfer.scanTo')} help={t('transfer.scanToHelp')} error={error} onSubmit={(c) => void scanBin(c, 'to')} pick="bin" testID="transfer-to" />
      ) : null}

      {step === 'confirm' && product ? (
        <>
          <Text style={styles.confirm}>{t('transfer.confirmText', { qty: f.qty(qty ?? 0), sku: product.sku, from: from?.code ?? '', to: to?.code ?? '' })}</Text>
          <ScanMessage message={error} tone="error" />
          <BigButton label={t('transfer.doIt')} onPress={() => void confirm()} disabled={busy} testID="transfer-confirm" />
        </>
      ) : null}

      {step === 'qty' ? <BigButton label={t('common.next')} onPress={() => setQtyDone(true)} disabled={issue !== null || qty === null} testID="transfer-next" /> : null}
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      {step !== 'from' ? <BigButton label={t('transfer.restart')} variant="secondary" onPress={reset} disabled={busy} /> : null}
      <View style={styles.bottom}>
        <BigButton label={t('common.back')} variant="secondary" onPress={() => router.replace('/home')} disabled={busy} />
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
  confirm: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  field: { gap: spacing.xs },
  bottom: { marginTop: 'auto' },
  input: { minHeight: 56, borderWidth: 2, borderColor: colors.line, borderRadius: 12, paddingHorizontal: spacing.md, fontSize: 20, color: colors.text, backgroundColor: colors.panelAlt },
})
