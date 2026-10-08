import { useState } from 'react'
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { canSeeSystemQty, useMyPermissions } from '../kernel/auth/permissions'
import { useSession } from '../kernel/auth/useSession'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { useFormat } from '../kernel/format/useFormat'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { ApiError, BIN_CONTENT_MAX_PAGES, BIN_CONTENT_PAGE, fetchBinContents, resolveLookupBin, searchBalances } from '../features/lookup/lookupApi'
import { aggregateBinContents, minutesAgo, type BalanceRow, type BinContentItem } from '../features/lookup/lookupLogic'
import { BinContentsList } from '../features/lookup/BinContentsList'

/** Producto o búsqueda libre: filas de saldo (como antes). Posición (Lote A8): lo que hay en ella, una fila por producto. */
type Result =
  | { kind: 'rows'; title: string; rows: BalanceRow[]; fromCache: boolean; fetchedAtUtc: string }
  | { kind: 'bin'; title: string; items: BinContentItem[]; fromCache: boolean; fetchedAtUtc: string; truncated: boolean }

/** Pantalla 7 (docs/mobile/app-almacen-plan.md §2): un solo campo (producto o posición), saldos en línea con
 *  respaldo en caché por si no hay señal (docs/lote8A-app-decisiones.md, segunda entrega).
 *  Lote A8: escanear una POSICIÓN muestra en vivo la lista de lo que el sistema dice que hay en ella (BinContentsList), con las
 *  cantidades del sistema solo para quien tiene warehouse.count (como en el conteo). Producto y búsqueda libre, como antes. */
export default function LookupScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const { device } = useSession()
  const activeWarehouse = useActiveWarehouse()
  const warehousePublicId = activeWarehouse.publicId
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<Result | null>(null)
  const permissions = useMyPermissions()
  const showQty = canSeeSystemQty(permissions)

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
      // Lote A8: si no es un producto y sí una posición, la lista de lo que hay en ella (por su id, no por texto libre)
      const binMatch = product ? null : await resolveLookupBin(warehousePublicId!, code)
      if (binMatch?.kind === 'inactive') {
        setError(t('lookup.binInactive', { bin: binMatch.code }))
        vibrateError()
        return
      }
      if (binMatch?.kind === 'bin') {
        const found = await fetchBinContents(warehousePublicId!, binMatch.bin)
        setResult({
          kind: 'bin',
          title: t('lookup.binResult', { bin: binMatch.bin.code }),
          items: aggregateBinContents(found.rows),
          fromCache: found.fromCache,
          fetchedAtUtc: found.fetchedAtUtc,
          truncated: found.truncated,
        })
        vibrateOk()
        return
      }
      const found = await searchBalances(warehousePublicId!, code, product?.publicId ?? null)
      if (found.rows.length === 0) {
        setError(t('lookup.notFound'))
        vibrateError()
        return
      }
      const title = product ? t('lookup.productResult') : t('lookup.searchResult', { code })
      setResult({ kind: 'rows', title, rows: found.rows, fromCache: found.fromCache, fetchedAtUtc: found.fetchedAtUtc })
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
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('lookup.title')}</Text>
      <ScanField label={t('lookup.scanLabel')} help={t('lookup.scanHelp')} error={error} onSubmit={scan} pick="any" />
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      {result ? (
        <>
          <Text style={styles.label}>{result.title}</Text>
          {result.fromCache ? <Text style={styles.help}>{t('lookup.cachedNote', { time: f.when(result.fetchedAtUtc), minutes: minutesAgo(result.fetchedAtUtc, new Date()) })}</Text> : null}
          {result.kind === 'bin' ? (
            <>
              <BinContentsList key={`${result.title}|${result.fetchedAtUtc}`} items={result.items} showQty={showQty} />
              {result.truncated ? <Text style={styles.help}>{t('lookup.binTruncated', { max: BIN_CONTENT_PAGE * BIN_CONTENT_MAX_PAGES })}</Text> : null}
            </>
          ) : (
            <LineList
              items={result.rows.map((r) => ({
                id: r.id,
                title: `${r.sku} · ${r.productName}`,
                subtitle: [r.binCode, r.lotNumber, `${t('lookup.onHand')}: ${f.qty(r.qtyOnHand)}`, `${t('lookup.available')}: ${f.qty(r.qtyAvailable)}`].filter(Boolean).join(' · '),
              }))}
              removeLabel={t('common.remove')}
              emptyLabel={t('lookup.empty')}
            />
          )}
        </>
      ) : null}
      {/* 2026-10-01 (Luis): al final de todo lo que hay en pantalla, también debajo del resultado */}
      <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
})
