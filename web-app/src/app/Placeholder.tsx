// Pantalla "pendiente" de un ítem del menú que todavía no tiene pantalla real (el `stub` de la maqueta): título y
// subtítulo del ítem, aviso de que llega en un lote posterior y "Abrir otra pantalla" (abre la paleta de comandos).
// Se declara en routes.tsx con `pending({ path, perm, module, nav })`; al llegar la pantalla real solo se cambia el `element`.
import type { ReactNode } from 'react'
import { useT } from '../kernel/i18n/useT'
import { BrandMark } from '../kernel/ui/Brand'
import { openCommandPalette } from '../kernel/ui/commandPaletteStore'
import { IconChev } from './icons'
import { navSubtitleKey, navTitleKey } from './navigation'

export interface PlaceholderProps {
  /** Clave del ítem del menú (`nav.<key>.title` / `.subtitle`). */
  navKey: string
  /** Ícono del ítem (el del grupo del menú). */
  icon?: ReactNode
  /** Marca pequeña de Teikem arriba del aviso; por defecto el símbolo (`BrandMark`, 40 px). `null` = sin marca. */
  brand?: ReactNode
}

export default function Placeholder({ navKey, icon, brand = <BrandMark size={40} alt="" /> }: PlaceholderProps) {
  const t = useT()
  const title = t(navTitleKey(navKey))
  return (
    <section data-testid="placeholder-screen">
      <div className="head">
        <div>
          <h1>{title}</h1>
          <p>{t(navSubtitleKey(navKey))}</p>
        </div>
      </div>
      <div className="empty pending">
        <div>
          {brand != null && <div className="pending-brand">{brand}</div>}
          {icon != null && (
            <div className="ic" aria-hidden="true">
              {icon}
            </div>
          )}
          <p className="pending-title">{title}</p>
          <p>{t('shell.placeholder.body')}</p>
          <button type="button" className="btn flow" onClick={openCommandPalette}>
            {t('shell.placeholder.another')}
            <IconChev />
          </button>
        </div>
      </div>
    </section>
  )
}
