import { useState } from 'react'
import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { useFormat } from '../kernel/format/useFormat'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { ApiError, searchBalances } from '../features/lookup/lookupApi'
import { minutesAgo, type BalanceRow } from '../features/lookup/lookupLogic'

type Result = { title: string; rows: BalanceRow[]; fromCache: boolean; fetchedAtUtc: string }

/** Pantalla 7 (docs/mobile/app-almacen-plan.md §2): un solo campo (producto o posición), saldos en línea con
 *  respaldo en caché por si no hay señal (docs/lote8A-app-decisiones.md, segunda entrega). */
export default function LookupScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const { device } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<Result | null>(null)

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  async function scan(code: string) {
    setError(null)
    setResult(null)
    setBusy(true)
    try {
      const product = findProductByCode(code)
      const found = await searchBalances(warehousePublicId!, code, product?.publicId ?? null)
      if (found.rows.length === 0) {
        setError(t('lookup.notFound'))
        vibrateError()
        return
      }
      const title = product ? t('lookup.productResult') : t('lookup.binResult', { bin: code })
      setResult({ title, rows: found.rows, fromCache: found.fromCache, fetchedAtUtc: found.fetchedAtUtc })
      vibrateOk()
    } catch (err) {
      if (err instanceof ApiError && err.code === 'network') {
        setError(t('lookup.noNetworkNoCache'))
      } else {
        setError(err instanceof ApiError ? err.title : t('errors.network'))
      }
      vibrateError()
    } finally {
      setBusy(false)
    }
  }

  return (
    <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>{t('lookup.title')}</Text>
      <ScanField label={t('lookup.scanLabel')} help={t('lookup.scanHelp')} error={error} onSubmit={scan} />
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      {result ? (
        <>
          <Text style={styles.label}>{result.title}</Text>
          {result.fromCache ? <Text style={styles.help}>{t('lookup.cachedNote', { time: f.when(result.fetchedAtUtc), minutes: minutesAgo(result.fetchedAtUtc, new Date()) })}</Text> : null}
          <LineList
            items={result.rows.map((r) => ({
              id: r.id,
              title: `${r.sku} · ${r.productName}`,
              subtitle: [r.binCode, r.lotNumber, `${t('lookup.onHand')}: ${f.qty(r.qtyOnHand)}`, `${t('lookup.available')}: ${f.qty(r.qtyAvailable)}`].filter(Boolean).join(' · '),
            }))}
            removeLabel={t('common.remove')}
            emptyLabel={t('lookup.empty')}
          />
        </>
      ) : null}
      {/* 2026-10-01 (Luis): al final de todo lo que hay en pantalla, también debajo del resultado */}
      <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
})
