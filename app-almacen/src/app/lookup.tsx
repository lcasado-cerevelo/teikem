import { useRef, useState } from 'react'
import { StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { canSeeSystemQty, useMyPermissions } from '../kernel/auth/permissions'
import { useSession } from '../kernel/auth/useSession'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { useFormat } from '../kernel/format/useFormat'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { BIN_CONTENT_MAX_PAGES, BIN_CONTENT_PAGE } from '../features/lookup/lookupApi'
import { applyOnline, lookupLocal, lookupOnline, type LookupResult } from '../features/lookup/lookupFlow'
import { minutesAgo } from '../features/lookup/lookupLogic'
import { RefreshNote, type RefreshState } from '../kernel/ui/RefreshNote'
import { BinContentsList } from '../features/lookup/BinContentsList'
import { BalanceRowsList } from '../features/lookup/BalanceRowsList'

/** Pantalla 7 (docs/mobile/app-almacen-plan.md §2): un solo campo (producto o posición).
 *  Señal débil (2026-10-10): muestra AL INSTANTE lo que el aparato ya tiene (saldos sincronizados) y en paralelo consulta al servidor
 *  (lookupFlow.ts); cuando llega, la pantalla se actualiza y el indicador pasa a «Al día». Con señal floja solo tarda la actualización.
 *  Lote A8: escanear una POSICIÓN muestra lo que hay en ella (BinContentsList), con las cantidades del sistema solo para quien tiene
 *  warehouse.count (como en el conteo). Producto y búsqueda libre, como antes. */
export default function LookupScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const { device } = useSession()
  const activeWarehouse = useActiveWarehouse()
  const warehousePublicId = activeWarehouse.publicId
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState<RefreshState>('idle')
  const [result, setResult] = useState<LookupResult | null>(null)
  // cada escaneo tiene su número: la respuesta tardía de uno anterior no pisa lo que se ve ahora
  const scanSeq = useRef(0)
  const permissions = useMyPermissions()
  const showQty = canSeeSystemQty(permissions)
  // 2026-10-10: «Mover» (abre Transferir con la posición y el producto ya puestos) solo con el permiso warehouse.transfer
  const canTransfer = permissions?.includes('warehouse.transfer') ?? false
  // «Ajustar» (cambia solo la cantidad de la posición) con el permiso warehouse.adjust, que el administrador da a un rol propio
  const canAdjust = permissions?.includes('warehouse.adjust') ?? false

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  async function scan(code: string) {
    const seq = ++scanSeq.current
    setError(null)
    setResult(null)
    // 1) lo que el aparato ya sabe, al instante
    const local = lookupLocal(warehousePublicId!, code)
    if (local) {
      setResult(local)
      vibrateOk()
    }
    // 2) en paralelo, el servidor: cuando llegue, la pantalla se actualiza
    setRefresh('syncing')
    const outcome = await lookupOnline(warehousePublicId!, code)
    if (seq !== scanSeq.current) return
    const next = applyOnline(local, outcome)
    setResult(next.result)
    setRefresh(next.refresh)
    if (next.error) {
      setError(
        next.error.key === 'notFound'
          ? t('lookup.notFound')
          : next.error.key === 'binInactive'
            ? t('lookup.binInactive', { bin: next.error.code })
            : next.error.key === 'noNetworkNoCache'
              ? t('lookup.noNetworkNoCache')
              : next.error.message || t('errors.network'),
      )
      vibrateError()
    } else if (!local && next.result) {
      vibrateOk()
    }
  }

  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('lookup.title')}</Text>
      <ScanField label={t('lookup.scanLabel')} help={t('lookup.scanHelp')} error={error} onSubmit={scan} pick="any" />
      <RefreshNote state={refresh} />
      {result ? (
        <>
          <Text style={styles.label}>{t(result.titleKey, result.titleParams)}</Text>
          {result.source !== 'live' && refresh !== 'syncing' ? (
            <Text style={styles.help}>{t(result.source === 'local' ? 'lookup.localNote' : 'lookup.cachedNote', { time: f.when(result.asOfUtc), minutes: minutesAgo(result.asOfUtc, new Date()) })}</Text>
          ) : null}
          {result.kind === 'bin' ? (
            <>
              <BinContentsList
                key={`${result.titleKey}|${result.bin.code}`}
                items={result.items}
                showQty={showQty}
                onMove={
                  canTransfer
                    ? (item) => router.push({ pathname: '/transfer', params: { fromBinId: String(result.bin.id), fromBinCode: result.bin.code, productPublicId: item.productPublicId } })
                    : undefined
                }
                onAdjust={
                  canAdjust
                    ? (item) => router.push({ pathname: '/adjust', params: { fromBinId: String(result.bin.id), fromBinCode: result.bin.code, productPublicId: item.productPublicId } })
                    : undefined
                }
              />
              {result.truncated ? <Text style={styles.help}>{t('lookup.binTruncated', { max: BIN_CONTENT_PAGE * BIN_CONTENT_MAX_PAGES })}</Text> : null}
            </>
          ) : (
            <BalanceRowsList
              rows={result.rows}
              onMove={
                canTransfer
                  ? (r) => router.push({ pathname: '/transfer', params: { fromBinId: String(r.binId), fromBinCode: r.binCode ?? '', productPublicId: r.productPublicId } })
                  : undefined
              }
              onAdjust={
                canAdjust
                  ? (r) => router.push({ pathname: '/adjust', params: { fromBinId: String(r.binId), fromBinCode: r.binCode ?? '', productPublicId: r.productPublicId } })
                  : undefined
              }
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
