// Lote 247 (Lote C) — escoger una cadena activa para agregarla a una lista de precios u oferta («Cadenas», «Incluir», «Excluir»). Es un selector simple: las
// cadenas de la compañía son pocas. Al escoger una llama `onPick(publicId)` y vuelve a «Escoger una cadena»; las que ya están (`exclude`) no salen.
import { useId } from 'react'
import { useT } from '../../kernel/i18n'
import { useChains } from './chainsApi'

export function ChainPicker({ label, exclude, onPick, disabled, id }: { label: string; exclude: readonly string[]; onPick: (publicId: string) => void; disabled?: boolean; id?: string }) {
  const t = useT()
  const auto = useId()
  const htmlId = id ?? auto
  const chains = useChains(false, '')
  const options = (chains.data ?? []).filter((c) => c.isActive && c.publicId && !exclude.includes(c.publicId))
  return (
    <div className="f chn-picker">
      <label htmlFor={htmlId}>{label}</label>
      <select
        id={htmlId}
        value=""
        disabled={disabled || chains.isPending}
        onChange={(e) => {
          if (e.target.value) onPick(e.target.value)
        }}
      >
        <option value="">{t('chains.picker.placeholder')}</option>
        {options.map((c) => (
          <option key={c.publicId} value={c.publicId}>
            {c.name}
          </option>
        ))}
      </select>
    </div>
  )
}
