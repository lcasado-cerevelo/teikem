// Lógica pura del Cotizador de tarifas del cliente (F-A3, manual 02 cap. 5.5): validación espejo de RateService.QuoteAsync, request,
// renombrado de los errores del servidor (`lines[i].pieces` → `lines.i.pieces` del formulario) y desglose del resultado.
// Sin React ni red, para probarla sola (quoteRules.test.ts).
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import { ApiError } from '../../kernel/api/problem'

type Schemas = components['schemas']
export type RateQuote = Schemas['RateQuoteDto']
export type RateQuoteRequest = Schemas['RateQuoteRequest']
export type QuoteLine = Schemas['QuoteLineDto']

export const SERVICE_TYPE_DOMAIN = 'ServiceType'
export const PACKAGE_TYPE_DOMAIN = 'PackageType'

export interface QuoteLineValues {
  serviceType: string
  packageType: string
  pieces?: number | null
}
export interface QuoteValues {
  lines: QuoteLineValues[]
  codAmount?: number | null
  asOf: string
}

export interface QuoteMessages {
  serviceRequired: string
  packageRequired: string
  piecesMin: string
  piecesInt: string
  codMin: string
  repeated: string
}

export function emptyQuoteLine(): QuoteLineValues {
  return { serviceType: '', packageType: '', pieces: 1 }
}

export function emptyQuote(): QuoteValues {
  return { lines: [emptyQuoteLine()], codAmount: null, asOf: '' }
}

export function quoteSchema(m: QuoteMessages) {
  return z
    .object({
      lines: z
        .array(
          z.object({
            serviceType: z.string().min(1, m.serviceRequired),
            packageType: z.string().min(1, m.packageRequired),
            pieces: z.number(m.piecesMin).int(m.piecesInt).min(1, m.piecesMin).nullable(),
          }),
        )
        .min(1),
      codAmount: z.number().min(0, m.codMin).nullable(),
      asOf: z.string(),
    })
    .superRefine((v, ctx) => {
      // Una sola línea por (servicio, paquete): dos del mismo par cobrarían dos bases en vez de base + pieza extra (mismo texto que el servidor).
      const seen = new Set<string>()
      v.lines.forEach((l, i) => {
        if (l.pieces == null) ctx.addIssue({ code: 'custom', path: ['lines', i, 'pieces'], message: m.piecesMin })
        if (!l.serviceType || !l.packageType) return
        const key = `${l.serviceType}|${l.packageType}`.toUpperCase()
        if (seen.has(key)) ctx.addIssue({ code: 'custom', path: ['lines', i, 'packageType'], message: m.repeated })
        seen.add(key)
      })
    })
}

export function buildQuoteRequest(clientPublicId: string, v: QuoteValues): RateQuoteRequest {
  return {
    clientPublicId,
    lines: v.lines.map((l) => ({ serviceType: l.serviceType, packageType: l.packageType, pieces: l.pieces ?? 0 })),
    codAmount: v.codAmount ?? null,
    asOf: v.asOf || null,
  }
}

/** `lines[2].pieces` → `lines.2.pieces`; `lines[1]` (par repetido) → `lines.1.packageType`; el resto, tal cual. */
export function remapQuoteErrors(errors: Record<string, string[]>): Record<string, string[]> {
  const out: Record<string, string[]> = {}
  for (const [key, messages] of Object.entries(errors)) {
    const m = /^lines\[(\d+)\](?:\.(\w+))?$/.exec(key)
    out[m ? `lines.${m[1]}.${m[2] ?? 'packageType'}` : key] = messages
  }
  return out
}

/** Relanza un ApiError con los errores de campo renombrados (para que `Form` los pinte bajo el campo correcto). */
export function rethrowQuoteError(err: unknown): never {
  if (err instanceof ApiError) {
    const mapped = new ApiError(err.status, { title: err.title, code: err.code, errors: remapQuoteErrors(err.errors) })
    throw mapped
  }
  throw err
}

/** Origen de un monto (baseSource / extraSource del servidor). */
export type QuoteSource = 'CONTRACT' | 'GENERIC' | 'NONE'
export function sourceOf(value: string | null | undefined): QuoteSource {
  const v = (value ?? '').toUpperCase()
  return v === 'CONTRACT' || v === 'GENERIC' ? v : 'NONE'
}

/**
 * Subtotal de una línea SIN despacho ni COD. El servidor suma el despacho y el COD al total de la PRIMERA línea; para mostrarlos como
 * renglones propios se resta aquí (base + piezas extra).
 */
export function lineSubtotal(line: Pick<QuoteLine, 'baseRate' | 'extraPieces'>): number {
  return Math.round(((line.baseRate ?? 0) + (line.extraPieces ?? 0)) * 10000) / 10000
}

/** Líneas que quedaron sin tarifa base (ni de contrato ni genérica): el servidor las cotiza en 0 sin error. */
export function linesWithoutRate(quote: Pick<RateQuote, 'lines'>): QuoteLine[] {
  return (quote.lines ?? []).filter((l) => sourceOf(l.baseSource) === 'NONE')
}
