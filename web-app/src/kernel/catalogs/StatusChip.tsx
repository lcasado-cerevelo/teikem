import { useT } from '../i18n'
import { useStatuses } from './api'
import { statusChipStyle } from './chipStyle'
import { sameCode } from './types'
import './catalogs.css'

export interface StatusChipProps {
  /** Dominio de estatus (ej. `OrderStatus`). */
  domain: string
  /** InternalCode del estatus; vacío → "Sin estatus". */
  code: string | null | undefined
  /** Etiqueta de respaldo mientras carga el catálogo (ej. la que ya trae el DTO). */
  label?: string | null
}

/** Píldora de estatus con la etiqueta y el color del tenant. */
export function StatusChip({ domain, code, label }: StatusChipProps) {
  const t = useT()
  const { data } = useStatuses(domain, { includeDisabled: true })
  if (!code) return <span className="chip">{t('status.none')}</span>
  const status = data?.find((s) => sameCode(s.code, code))
  return (
    <span className="chip" style={statusChipStyle(status?.color)} data-code={code}>
      {status?.label ?? label ?? code}
    </span>
  )
}
