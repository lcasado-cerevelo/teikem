import { useEffect, useRef, useState } from 'react'
import { LANGS, type Lang } from '../kernel/i18n/i18n'
import { useT } from '../kernel/i18n/useT'
import { IconGlobe } from './icons'

interface Props {
  lang: Lang
  onChange: (lang: Lang) => void
}

/** Selector de idioma de la cabecera (menú desplegable de la maqueta, `.langdd`). */
export function LangSelect({ lang, onChange }: Props) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent | KeyboardEvent) => {
      if (e instanceof KeyboardEvent ? e.key === 'Escape' : !ref.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', close)
    }
  }, [open])

  return (
    <div className={open ? 'langdd open' : 'langdd'} ref={ref}>
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label={t('shell.language')} onClick={() => setOpen((o) => !o)}>
        <IconGlobe />
        <span className="lbl">{lang.toUpperCase()}</span>
      </button>
      <div className="menu" role="menu">
        {LANGS.map((l) => (
          <button
            key={l}
            type="button"
            role="menuitemradio"
            aria-checked={l === lang}
            className={l === lang ? 'on' : undefined}
            onClick={() => {
              onChange(l)
              setOpen(false)
            }}
          >
            {t(`lang.${l}`)}
          </button>
        ))}
      </div>
    </div>
  )
}
