// Lote F8a (P2) — modo Organizar del Pulso (mismo componente para `mine` y `company`). Copia local editable de paneles,
// indicadores y gráficos: cada panel y, dentro de INDICATORS/CHARTS, cada elemento se sube/baja (▲ ▼, o ↑/↓ con el foco
// en el asa), se arrastra con el ratón en escritorio (drag & drop nativo de HTML5, sin librería; en táctil solo botones) y
// se oculta o vuelve a mostrar (se queda en su sitio, atenuado). "Listo" hace UN solo
// `PUT /api/v1/analytics/pulse/layout?scope=…` con TODOS los paneles y elementos (orden = índice × 10); "Cancelar" descarta.
import { useEffect, useRef, useState, type DragEvent, type KeyboardEvent, type ReactNode } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { toast } from '../../kernel/ui/toast'
import { useMediaQuery } from '../../kernel/ui/useMediaQuery'
import { useSaveLayout } from './api'
import { IconEye, IconEyeOff, IconGrip } from './pulseIcons'
import { PULSE_PANELS } from './pulsePanels'
import {
  buildLayoutRequest,
  indicatorLines,
  initOrganizer,
  moduleGroup,
  moveEntry,
  sameLine,
  toggleEntry,
  type OrganizerItem,
  type OrganizerList,
  type PulseDto,
  type PulseScope,
} from './pulseLayout'
import './pulse.css'

/** Punteros sin ratón (táctil): no se arrastra, solo ▲ ▼. */
const COARSE_POINTER = '(pointer: coarse)'

type Control = 'handle' | 'up' | 'down'

export interface PulseOrganizerProps {
  scope: PulseScope
  /** Pulso cargado (GET /pulse): de aquí sale la copia local. */
  pulse: PulseDto
  /** Sale del modo Organizar (tras guardar o al cancelar). */
  onClose: () => void
}

interface DragSource {
  list: OrganizerList
  index: number
}

export function PulseOrganizer({ scope, pulse, onClose }: PulseOrganizerProps) {
  const t = useT()
  const [state, setState] = useState(() => initOrganizer(pulse))
  const save = useSaveLayout(scope)
  const coarse = useMediaQuery(COARSE_POINTER)
  const drag = useRef<DragSource | null>(null)
  const [over, setOver] = useState<DragSource | null>(null)
  // Tras mover con teclado o botones, el foco vuelve al mismo control del elemento movido (o a su asa si quedó deshabilitado).
  const pendingFocus = useRef<{ id: string; control: Control } | null>(null)
  const controls = useRef(new Map<string, HTMLButtonElement>())

  useEffect(() => {
    const f = pendingFocus.current
    if (!f) return
    pendingFocus.current = null
    const el = controls.current.get(`${f.id}|${f.control}`)
    const target = el && !el.disabled ? el : controls.current.get(`${f.id}|handle`)
    target?.focus()
  })

  const register = (id: string, control: Control) => (el: HTMLButtonElement | null) => {
    const k = `${id}|${control}`
    if (el) controls.current.set(k, el)
    else controls.current.delete(k)
  }

  const lengthOf = (list: OrganizerList) => state[list].length
  // Lote 15 (D8): un indicador solo se mueve dentro de su línea de módulo.
  const canMove = (list: OrganizerList, from: number, to: number) => to >= 0 && to < lengthOf(list) && from !== to && sameLine(state, list, from, to)
  const move = (list: OrganizerList, from: number, to: number, focus?: { id: string; control: Control }) => {
    if (!canMove(list, from, to)) return
    setState((s) => moveEntry(s, list, from, to))
    if (focus) pendingFocus.current = focus
  }
  const toggle = (list: OrganizerList, index: number) => setState((s) => toggleEntry(s, list, index))

  const busy = save.isPending
  const done = async () => {
    try {
      await save.mutateAsync(buildLayoutRequest(state))
      toast.success(t('analytics.pulse.organizer.saved'))
      onClose()
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  // ---- arrastrar (ratón): se arrastra la fila, pero solo si se tomó por el asa (la fila queda "armada" mientras el botón
  // del ratón está abajo sobre el asa; así funciona también en Firefox, que no arrastra un <button draggable>). La fila
  // entera es el destino, solo dentro de la misma lista. ----
  const [armed, setArmed] = useState<string | null>(null)
  useEffect(() => {
    if (!armed) return
    const disarm = () => setArmed(null)
    window.addEventListener('pointerup', disarm)
    return () => window.removeEventListener('pointerup', disarm)
  }, [armed])

  const dragProps = (list: OrganizerList, index: number, id: string) => ({
    draggable: !coarse && !busy && armed === id,
    onDragStart: (e: DragEvent<HTMLLIElement>) => {
      if (armed !== id) return
      e.stopPropagation()
      drag.current = { list, index }
      e.dataTransfer.effectAllowed = 'move'
      e.dataTransfer.setData('text/plain', id)
    },
    onDragEnd: (e: DragEvent<HTMLLIElement>) => {
      e.stopPropagation()
      drag.current = null
      setOver(null)
      setArmed(null)
    },
    onDragOver: (e: DragEvent<HTMLLIElement>) => {
      if (drag.current?.list !== list || !sameLine(state, list, drag.current.index, index)) return
      e.preventDefault()
      e.stopPropagation()
      e.dataTransfer.dropEffect = 'move'
      if (over?.list !== list || over.index !== index) setOver({ list, index })
    },
    onDrop: (e: DragEvent<HTMLLIElement>) => {
      const src = drag.current
      if (src?.list !== list) return
      e.preventDefault()
      e.stopPropagation()
      drag.current = null
      setOver(null)
      move(list, src.index, index)
    },
  })

  const title = scope === 'company' ? t('analytics.pulse.organizer.titleCompany') : t('analytics.pulse.organizer.titleMine')

  const row = (args: {
    list: OrganizerList
    index: number
    id: string
    name: string
    sub?: string
    isVisible: boolean
    children?: ReactNode
  }) => {
    const { list, index, id, name, sub, isVisible, children } = args
    const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>) => {
      if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
        e.preventDefault()
        move(list, index, e.key === 'ArrowUp' ? index - 1 : index + 1, { id, control: 'handle' })
      }
    }
    const classes = ['orgrow', isVisible ? '' : 'off', over?.list === list && over.index === index ? 'over' : ''].filter(Boolean).join(' ')
    return (
      <li key={id} className={classes} data-org={id} {...dragProps(list, index, id)}>
        <div className="orgh">
          <button
            type="button"
            className={coarse ? 'orghandle' : 'orghandle grab'}
            ref={register(id, 'handle')}
            aria-label={t('analytics.pulse.organizer.handle', { name })}
            title={t('analytics.pulse.organizer.handle', { name })}
            onKeyDown={onKeyDown}
            onPointerDown={() => {
              if (!coarse && !busy) setArmed(id)
            }}
            disabled={busy}
          >
            <IconGrip />
          </button>
          <span className="orgname">
            <span className="nm">{name}</span>
            {sub && <span className="sb">{sub}</span>}
          </span>
          {!isVisible && <Chip tone="cap">{t('analytics.pulse.organizer.hidden')}</Chip>}
          <span className="orgacts">
            <button
              type="button"
              className="btn sm"
              ref={register(id, 'up')}
              aria-label={t('analytics.pulse.organizer.moveUp', { name })}
              title={t('analytics.pulse.organizer.moveUp', { name })}
              disabled={busy || !canMove(list, index, index - 1)}
              onClick={() => move(list, index, index - 1, { id, control: 'up' })}
            >
              ▲
            </button>
            <button
              type="button"
              className="btn sm"
              ref={register(id, 'down')}
              aria-label={t('analytics.pulse.organizer.moveDown', { name })}
              title={t('analytics.pulse.organizer.moveDown', { name })}
              disabled={busy || !canMove(list, index, index + 1)}
              onClick={() => move(list, index, index + 1, { id, control: 'down' })}
            >
              ▼
            </button>
            <button
              type="button"
              className="btn sm"
              aria-label={t(isVisible ? 'analytics.pulse.organizer.hideFor' : 'analytics.pulse.organizer.showFor', { name })}
              disabled={busy}
              onClick={() => toggle(list, index)}
            >
              {isVisible ? <IconEyeOff /> : <IconEye />}
              {isVisible ? t('analytics.pulse.organizer.hide') : t('analytics.pulse.organizer.show')}
            </button>
          </span>
        </div>
        {children}
      </li>
    )
  }

  const itemRow = (list: 'indicators' | 'charts', it: OrganizerItem, index: number, withModule: boolean) =>
    row({
      list,
      index,
      id: `${list}:${it.id}`,
      name: it.name,
      sub: withModule ? t(`nav.groups.${moduleGroup(it.businessModule)}`) : undefined,
      isVisible: it.isVisible,
    })

  const itemsList = (list: 'indicators' | 'charts', items: OrganizerItem[], panelName: string) => {
    if (items.length === 0) return <p className="orgempty">{t('analytics.pulse.organizer.noItems')}</p>
    if (list === 'charts')
      return (
        <ol className="orgitems" aria-label={t('analytics.pulse.organizer.itemsLabel', { name: panelName })}>
          {items.map((it, i) => itemRow(list, it, i, true))}
        </ol>
      )
    // Lote 15 (D8): los indicadores, una lista por línea de módulo (como en el Pulso); se ordenan dentro de su línea. El
    // estado ya viene agrupado por línea (`initOrganizer`), así que el índice de cada uno es su posición en la lista plana.
    let offset = 0
    return (
      <div className="orglines">
        <p className="orghint">{t('analytics.pulse.organizer.linesHint')}</p>
        {indicatorLines(items).map((line) => {
          const start = offset
          offset += line.items.length
          const lineName = t(`nav.groups.${line.group}`)
          return (
            <div key={line.group} className="orgline" data-line={line.group}>
              <h3 className="orgline-h">{lineName}</h3>
              <ol className="orgitems" aria-label={t('analytics.pulse.organizer.lineLabel', { name: panelName, line: lineName })}>
                {line.items.map((it, i) => itemRow(list, it, start + i, false))}
              </ol>
            </div>
          )
        })}
      </div>
    )
  }

  return (
    <div className="pulse-org">
      <div className="orgbar" role="region" aria-label={title}>
        <div className="ttl">
          <strong>{title}</strong>
          <span className="hint">{t(coarse ? 'analytics.pulse.organizer.hintTouch' : 'analytics.pulse.organizer.hint')}</span>
        </div>
        <div className="acts">
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void done()} disabled={busy}>
            {busy ? t('common.loading') : t('analytics.pulse.organizer.done')}
          </button>
        </div>
      </div>
      {scope === 'company' && pulse.hasPersonalLayout && <p className="note orgnote">{t('analytics.pulse.organizer.personalNote')}</p>}
      {/* Lote 15 (D6): con una franja fijable, se explica qué queda fijo según dónde quede */}
      {state.panels.some((p) => PULSE_PANELS[p.key].pinnable) && <p className="pulse-muted orgnote">{t('analytics.pulse.organizer.pinHint')}</p>}
      <ol className="orglist" aria-label={t('analytics.pulse.organizer.panelsLabel')}>
        {state.panels.map((p, i) => {
          const entry = PULSE_PANELS[p.key]
          const name = t(entry.titleKey)
          return row({
            list: 'panels',
            index: i,
            id: `panel:${p.key}`,
            name,
            isVisible: p.isVisible,
            children: entry.items ? itemsList(entry.items, state[entry.items], name) : undefined,
          })
        })}
      </ol>
    </div>
  )
}
