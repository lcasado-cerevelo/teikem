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
import { ScanMessage } from '../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { QuantityField } from '../features/count/QuantityField'
import { parseQty } from '../features/count/countLogic'
import { fetchBinContents } from '../features/lookup/lookupApi'
import type { BalanceRow } from '../features/lookup/lookupLogic'
import { isBlockedZone } from '../features/transfer/transferLogic'
import { adjustQuantity } from '../features/adjust/adjustApi'
import { adjustIssue, afterQty, NOTE_MAX, reasonsFor, type AdjustDirection } from '../features/adjust/adjustLogic'

/**
 * 2026-10-10 — Ajustar: cambia SOLO la cantidad de una posición (sube o baja). Mover de una posición a otra es Transferir. Se abre desde Consultar con «Ajustar»
 * (llega con la posición y el producto ya puestos), con el permiso warehouse.adjust. Pide dirección, cantidad, motivo y nota (obligatoria). Necesita señal: sin cola.
 */
export default function AdjustScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const params = useLocalSearchParams<{ fromBinId?: string; fromBinCode?: string; productPublicId?: string }>()
  const warehousePublicId = useActiveWarehouse().publicId
  const permissions = useMyPermissions()
  const showSystemQty = canSeeSystemQty(permissions)
  const [rows, setRows] = useState<BalanceRow[]>([])
  const [loaded, setLoaded] = useState(false)
  const [lotId, setLotId] = useState<number | null>(null)
  const [direction, setDirection] = useState<AdjustDirection | null>(null)
  const [qtyText, setQtyText] = useState('')
  const [reason, setReason] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const binId = Number(params.fromBinId)
  const binCode = params.fromBinCode ?? ''
  const productPublicId = params.productPublicId ?? ''

  function fail(message: string) {
    setError(message)
    vibrateError()
  }
  function failure(err: unknown) {
    fail(isNetworkError(err) ? t('adjust.needSignal') : err instanceof ApiError ? err.title : t('errors.generic'))
  }

  useEffect(() => {
    if (!warehousePublicId || !binId || !productPublicId) return
    let alive = true
    setBusy(true)
    fetchBinContents(warehousePublicId, { id: binId, code: binCode })
      .then((found) => {
        if (!alive) return
        if (found.rows.some((r) => isBlockedZone(r.zoneTypeCode))) return fail(t('adjust.zoneBlocked', { bin: binCode }))
        const mine = found.rows.filter((r) => r.productPublicId === productPublicId)
        setRows(mine)
        const withLot = mine.filter((r) => r.lotId != null)
        setLotId(withLot.length === 1 ? (withLot[0].lotId ?? null) : null)
        setLoaded(true)
      })
      .catch((err) => alive && failure(err))
      .finally(() => alive && setBusy(false))
    return () => {
      alive = false
    }
    // solo al abrir la pantalla
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (!warehousePublicId || !binId || !productPublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
        <BigButton label={t('common.back')} variant="secondary" onPress={() => router.replace('/lookup')} />
      </View>
    )
  }

  const first = rows[0]
  const sku = first?.sku ?? ''
  const lots = rows.filter((r) => r.lotId != null)
  const needLot = lots.length > 1 && lotId == null
  const row = lots.length > 0 ? lots.find((r) => r.lotId === lotId) : rows.find((r) => r.lotId == null) ?? first
  const onHand = row?.qtyOnHand ?? 0
  const available = row?.qtyAvailable ?? 0
  const local = sku ? findProductByCode(sku) : null
  const serial = local?.trackingTypeCode === 'SERIAL'
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
    setBusy(true)
    setError(null)
    try {
      await adjustQuantity({ warehousePublicId: warehousePublicId!, productPublicId, binId, lotId: lots.length > 0 ? lotId : null, direction: dir, quantity: q, reason: why, note })
      setNotice(t('adjust.done', { dir: t(`adjust.${dir}`), qty: f.qty(q), sku, bin: binCode }))
      vibrateOk()
      setDirection(null)
      setQtyText('')
      setReason(null)
      setNote('')
      router.replace('/lookup')
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('adjust.title')}</Text>
      <ScanMessage message={notice} tone="ok" />
      <Text style={styles.summary}>{t('adjust.inBin', { bin: binCode })}</Text>
      {first ? <Text style={styles.product}>{`${first.sku} · ${first.productName}`}</Text> : null}
      <ScanMessage message={error} tone="error" />
      {busy && !loaded ? <ActivityIndicator color={colors.brand} /> : null}

      {serial ? <Text style={styles.error}>{t('adjust.serialNotSupported', { sku })}</Text> : null}

      {loaded && !serial && needLot ? (
        <>
          <Text style={styles.label}>{t('adjust.chooseLot')}</Text>
          <LineList
            items={lots.map((r) => ({ id: r.lotId ?? 0, title: r.lotNumber ?? String(r.lotId), subtitle: showSystemQty ? t('adjust.onHand', { qty: f.qty(r.qtyOnHand) }) : undefined }))}
            removeLabel={t('common.remove')}
            onPressItem={(id) => setLotId(Number(id))}
            pressLabel={(item) => t('adjust.useLot', { lot: item.title })}
          />
        </>
      ) : null}

      {loaded && !serial && !needLot && row ? (
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
        <BigButton label={t('common.back')} variant="secondary" onPress={() => router.replace('/lookup')} disabled={busy} />
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
  bottom: { marginTop: 'auto' },
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
