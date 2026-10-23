// Un teléfono guardado, con el formato de la compañía, que se puede tocar para llamar (en un celular abre el marcador con el número puesto).
import { useFormat } from '../../kernel/format'
import { telHref } from '../messages/links'

export function PhoneLink({ value, countryCallingCode, className }: { value: string; countryCallingCode?: string | null; className?: string }) {
  const f = useFormat()
  return (
    <a href={telHref(value, countryCallingCode)} className={className}>
      {f.phoneFor(value, countryCallingCode)}
    </a>
  )
}
