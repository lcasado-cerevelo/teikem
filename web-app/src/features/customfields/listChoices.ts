import { LOOKUP_SCOPE } from '../../kernel/custom-fields'
import type { ComboOption } from '../../kernel/ui'

/** Opciones del selector: primero las listas propias de la compañía y luego las del sistema; la elegida siempre aparece. */
export function listChoices(
  domains: { domainKey?: string | null; label?: string | null; scope?: number; isSystem?: boolean; isActive?: boolean }[] | undefined,
  selected: string,
  labels: { own: string; system: string },
): ComboOption[] {
  const options = (domains ?? [])
    .filter((d) => d.scope === LOOKUP_SCOPE && d.isActive !== false && !!d.domainKey)
    .map((d) => ({ value: d.domainKey ?? '', label: d.label || (d.domainKey ?? ''), hint: d.isSystem ? labels.system : labels.own, own: !d.isSystem }))
    .sort((a, b) => Number(b.own) - Number(a.own) || a.label.localeCompare(b.label))
    .map(({ value, label, hint }) => ({ value, label, hint }))
  if (selected !== '' && !options.some((o) => o.value === selected)) options.unshift({ value: selected, label: selected, hint: labels.own })
  return options
}
