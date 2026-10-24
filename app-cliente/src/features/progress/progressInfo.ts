// Del DTO del API (campos opcionales) a lo que pinta la barra; null si no hay avance que mostrar.
import type { ProgressInfo } from './ProgressBar'

export function toProgressInfo(p: { step?: number; total?: number; state?: string | null; statusLabel?: string | null; failedAttempts?: number | null } | null | undefined): ProgressInfo | null {
  if (!p || !p.total || p.total <= 0) return null
  return { step: p.step ?? 0, total: p.total, state: p.state ?? 'IN_PROGRESS', statusLabel: p.statusLabel ?? '', failedAttempts: p.failedAttempts ?? 0 }
}
