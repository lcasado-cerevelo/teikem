// Señal débil (2026-10-10) — Consultar trabaja primero con lo que el aparato ya tiene (saldos sincronizados, kernel/warehouse/localBalances.ts),
// lo muestra AL INSTANTE y en paralelo le pregunta al servidor; cuando llega, la pantalla se actualiza. La señal floja solo retrasa la
// actualización, nunca el resultado. Sin saldos locales (el aparato todavía no los ha bajado) se comporta como antes: espera al servidor.
import { findProductByCode } from '../../kernel/warehouse/productLookup'
import { balancesSyncedAtUtc, localBalancesForBin, localBalancesForProduct, localBalancesSearch } from '../../kernel/warehouse/localBalances'
import { findLocalBin } from '../count/countApi'
import { ApiError, fetchBinContents, resolveLookupBin, searchBalances } from './lookupApi'
import type { RefreshState } from '../../kernel/ui/RefreshNote'
import { aggregateBinContents, type BalanceRow, type BinContentItem } from './lookupLogic'

/** De dónde salen los datos que se ven: del aparato, de la última respuesta guardada o del servidor ahora mismo. */
export type LookupSource = 'local' | 'cache' | 'live'

/** Resultado de una consulta; el título se guarda como clave de texto para traducirlo al pintarlo. */
export type LookupResult =
  | { kind: 'rows'; titleKey: 'lookup.productResult' | 'lookup.searchResult'; titleParams: Record<string, string>; rows: BalanceRow[]; source: LookupSource; asOfUtc: string }
  | { kind: 'bin'; bin: { id: number; code: string }; titleKey: 'lookup.binResult'; titleParams: Record<string, string>; items: BinContentItem[]; source: LookupSource; asOfUtc: string; truncated: boolean }

/** Lo que dijo el servidor (o por qué no pudo decir nada). */
export type OnlineOutcome =
  | { status: 'ok'; result: LookupResult }
  | { status: 'notFound' }
  | { status: 'binInactive'; code: string }
  /** Sin señal o con señal que no alcanzó: si había una consulta guardada, viene aquí. */
  | { status: 'offline'; result: LookupResult | null }
  | { status: 'error'; message: string }

/** Lo que el aparato ya sabe de este código, sin red. null = no hay nada que mostrar todavía (o nunca bajó saldos). */
export function lookupLocal(warehousePublicId: string, code: string): LookupResult | null {
  const asOfUtc = balancesSyncedAtUtc(warehousePublicId)
  if (!asOfUtc) return null
  const product = findProductByCode(code)
  if (product) {
    const rows = localBalancesForProduct(warehousePublicId, product.publicId)
    return rows.length > 0 ? { kind: 'rows', titleKey: 'lookup.productResult', titleParams: {}, rows, source: 'local', asOfUtc } : null
  }
  const bin = findLocalBin(warehousePublicId, code)
  if (bin) {
    return {
      kind: 'bin',
      bin: { id: bin.id, code: bin.code },
      titleKey: 'lookup.binResult',
      titleParams: { bin: bin.code },
      items: aggregateBinContents(localBalancesForBin(warehousePublicId, bin.id)),
      source: 'local',
      asOfUtc,
      truncated: false,
    }
  }
  const rows = localBalancesSearch(warehousePublicId, code)
  return rows.length > 0 ? { kind: 'rows', titleKey: 'lookup.searchResult', titleParams: { code }, rows, source: 'local', asOfUtc } : null
}

function isNetwork(err: unknown): boolean {
  return err instanceof ApiError && err.code === 'network'
}

/** Pregunta al servidor (con la copia guardada de respaldo si no hay red). Nunca lanza: devuelve el resultado o la razón. */
export async function lookupOnline(warehousePublicId: string, code: string): Promise<OnlineOutcome> {
  try {
    const product = findProductByCode(code)
    // si no es un producto y sí una posición, lo que hay en ella (por su id, no por texto libre)
    const binMatch = product ? null : await resolveLookupBin(warehousePublicId, code)
    if (binMatch?.kind === 'inactive') return { status: 'binInactive', code: binMatch.code }
    if (binMatch?.kind === 'bin') {
      const found = await fetchBinContents(warehousePublicId, binMatch.bin)
      const result: LookupResult = {
        kind: 'bin',
        bin: { id: binMatch.bin.id, code: binMatch.bin.code },
        titleKey: 'lookup.binResult',
        titleParams: { bin: binMatch.bin.code },
        items: aggregateBinContents(found.rows),
        source: found.fromCache ? 'cache' : 'live',
        asOfUtc: found.fetchedAtUtc,
        truncated: found.truncated,
      }
      return found.fromCache ? { status: 'offline', result } : { status: 'ok', result }
    }
    const found = await searchBalances(warehousePublicId, code, product?.publicId ?? null)
    if (found.rows.length === 0) return { status: 'notFound' }
    const result: LookupResult = {
      kind: 'rows',
      titleKey: product ? 'lookup.productResult' : 'lookup.searchResult',
      titleParams: product ? {} : { code },
      rows: found.rows,
      source: found.fromCache ? 'cache' : 'live',
      asOfUtc: found.fetchedAtUtc,
    }
    return found.fromCache ? { status: 'offline', result } : { status: 'ok', result }
  } catch (err) {
    if (isNetwork(err)) return { status: 'offline', result: null }
    return { status: 'error', message: err instanceof ApiError ? err.title : '' }
  }
}


/** Qué hace la pantalla con la respuesta del servidor, dado lo que ya muestra. Pura: se prueba sin pantalla. */
export function applyOnline(
  shown: LookupResult | null,
  outcome: OnlineOutcome,
): { result: LookupResult | null; refresh: RefreshState; error: { key: 'notFound' } | { key: 'binInactive'; code: string } | { key: 'noNetworkNoCache' } | { key: 'generic'; message: string } | null } {
  switch (outcome.status) {
    case 'ok':
      return { result: outcome.result, refresh: 'done', error: null }
    case 'notFound':
      // el servidor manda: si lo local decía que sí había algo, estaba desactualizado
      return { result: null, refresh: 'done', error: { key: 'notFound' } }
    case 'binInactive':
      return { result: null, refresh: 'done', error: { key: 'binInactive', code: outcome.code } }
    case 'offline':
      // se queda lo que ya se veía; si no había nada, la última consulta guardada (si existe)
      if (shown) return { result: shown, refresh: 'offline', error: null }
      return outcome.result
        ? { result: outcome.result, refresh: 'offline', error: null }
        : { result: null, refresh: 'offline', error: { key: 'noNetworkNoCache' } }
    case 'error':
      return shown
        ? { result: shown, refresh: 'offline', error: null }
        : { result: null, refresh: 'offline', error: { key: 'generic', message: outcome.message } }
  }
}
