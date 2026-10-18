// Lote 115 — cargo por devolución del contrato (opcional): por orden, por tipo de paquete o por pieza. Reglas puras del formulario (el servidor manda al final con los mismos mensajes).
export const RETURN_FEE_MODES = ['ORDER', 'PACKAGE_TYPE', 'PIECE'] as const
export type ReturnFeeMode = (typeof RETURN_FEE_MODES)[number]

export interface ReturnFeeRow {
  packageType: string
  amount: string
}

export interface ReturnFeeForm {
  mode: ReturnFeeMode | ''
  amount: string
  rows: ReturnFeeRow[]
}

/** Número con coma o punto → número ≥ 0; vacío → null; inválido o negativo → NaN. */
export function parseFeeAmount(raw: string): number | null {
  const s = raw.trim().replace(',', '.')
  if (s === '') return null
  return /^\d+(\.\d{1,2})?$/.test(s) ? Number(s) : Number.NaN
}

export function returnFeeError(f: ReturnFeeForm): 'amountRequired' | 'badAmount' | 'byTypeRequired' | 'duplicateType' | 'typeRequired' | null {
  if (f.mode === '') return null
  if (f.mode === 'ORDER' || f.mode === 'PIECE') {
    const a = parseFeeAmount(f.amount)
    if (a === null) return 'amountRequired'
    if (Number.isNaN(a)) return 'badAmount'
    return null
  }
  const rows = f.rows.filter((r) => r.packageType !== '' || r.amount.trim() !== '')
  if (rows.length === 0) return 'byTypeRequired'
  const seen = new Set<string>()
  for (const r of rows) {
    if (r.packageType === '') return 'typeRequired'
    const a = parseFeeAmount(r.amount)
    if (a === null) return 'amountRequired'
    if (Number.isNaN(a)) return 'badAmount'
    if (seen.has(r.packageType)) return 'duplicateType'
    seen.add(r.packageType)
  }
  return null
}

export function toReturnFeeRequest(f: ReturnFeeForm) {
  if (f.mode === '') return { mode: null, amount: null, byType: null }
  if (f.mode === 'PACKAGE_TYPE')
    return {
      mode: f.mode,
      amount: null,
      byType: f.rows.filter((r) => r.packageType !== '').map((r) => ({ packageType: r.packageType, amount: parseFeeAmount(r.amount) ?? 0 })),
    }
  return { mode: f.mode, amount: parseFeeAmount(f.amount), byType: null }
}

export function returnFeeFormOf(fee: { mode?: string | null; amount?: number | null; byType?: { packageType?: string | null; amount?: number }[] | null } | null | undefined): ReturnFeeForm {
  const mode = (RETURN_FEE_MODES as readonly string[]).includes(fee?.mode ?? '') ? (fee!.mode as ReturnFeeMode) : ''
  return {
    mode,
    amount: fee?.amount != null ? String(fee.amount) : '',
    rows: (fee?.byType ?? []).map((r) => ({ packageType: r.packageType ?? '', amount: String(r.amount ?? '') })),
  }
}
