// Estado de «consignatario nuevo en línea» (Lote 213) de una pantalla de captura de envíos: si el formulario en línea está abierto (y con qué
// nombre), el texto con el que se reabre el combo al cancelar, y a dónde va el foco al terminar. Todo sin ratón: al elegir «+ Nuevo» el
// formulario toma el foco solo (lo hace él mismo al montarse); al grabar, el foco pasa al siguiente campo; con Escape, vuelve al combo con
// el texto que había. `ConsigneePicker` lo recibe en `inline`.
import { useCallback, useEffect, useState } from 'react'

const FOCUSABLE = 'input:not([type="hidden"]), select, textarea, button, a[href], [tabindex]'

/** Siguiente control enfocable en el documento después de `anchor` y fuera de él (por posición en el DOM). */
export function nextFocusableAfter(anchor: Element | null): HTMLElement | null {
  if (!anchor) return null
  const root = anchor.closest('form') ?? document.body
  for (const el of Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE))) {
    if (anchor.contains(el)) continue
    if (!(anchor.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING)) continue
    if ((el as HTMLInputElement).disabled || el.getAttribute('tabindex') === '-1' || el.getAttribute('aria-hidden') === 'true') continue
    return el
  }
  return null
}

export interface InlineConsigneeOptions {
  /** Selector CSS del input del combo del consignatario (`#qk-cons`). */
  comboSelector: string
  /** id del control al que pasa el foco tras grabar (la rápida: `qk-pkg`). Sin él, el siguiente enfocable después del combo. */
  nextId?: string
  /** Selector CSS del control al que pasa el foco tras grabar cuando no hay `nextId` (la detallada: el tipo del primer paquete); si no existe en pantalla, el siguiente enfocable. */
  nextSelector?: string
}

export interface InlineConsigneeController {
  /** Nombre escrito con el que se abrió el formulario; null = cerrado. */
  draft: string | null
  /** Texto con el que se reabre el combo (tras cancelar). */
  seedText: string
  /** El combo pidió crear lo escrito: abre el formulario. */
  begin: (text: string) => void
  /** Escape: cierra y devuelve el foco al combo con el texto que había. */
  cancel: () => void
  /** El consignatario ya quedó grabado y elegido: cierra y pasa el foco al siguiente campo. */
  done: () => void
  /** Cierra sin mover el foco (cambió el cliente, se guardó la orden…). */
  reset: () => void
  /** Olvida el texto de reapertura (se eligió algo). */
  clearSeed: () => void
  /** El foco sale del formulario con Tab desde su último campo: va al siguiente de la pantalla. */
  tabOut: () => void
}

export function useInlineConsignee({ comboSelector, nextId, nextSelector }: InlineConsigneeOptions): InlineConsigneeController {
  const [draft, setDraft] = useState<string | null>(null)
  const [seedText, setSeedText] = useState('')
  const [pending, setPending] = useState<{ kind: 'combo' | 'next' } | null>(null)

  // el foco se mueve cuando el formulario ya se quitó del DOM (cada petición es un objeto nuevo: el efecto corre una vez por petición)
  useEffect(() => {
    if (!pending) return
    const combo = document.querySelector<HTMLElement>(comboSelector)
    if (pending.kind === 'combo') combo?.focus()
    else (nextId ? document.getElementById(nextId) : (nextSelector ? document.querySelector<HTMLElement>(nextSelector) : null) ?? nextFocusableAfter(combo?.closest('.oe-consrow') ?? combo))?.focus()
  }, [pending, comboSelector, nextId, nextSelector])

  const begin = useCallback((text: string) => {
    setSeedText('')
    setDraft(text)
  }, [])
  const cancel = useCallback(() => {
    setSeedText(draft ?? '')
    setDraft(null)
    setPending({ kind: 'combo' })
  }, [draft])
  const done = useCallback(() => {
    setDraft(null)
    setSeedText('')
    setPending({ kind: 'next' })
  }, [])
  const reset = useCallback(() => {
    setDraft(null)
    setSeedText('')
  }, [])
  const clearSeed = useCallback(() => setSeedText(''), [])
  const tabOut = useCallback(() => {
    const el = nextId ? document.getElementById(nextId) : (nextSelector ? document.querySelector<HTMLElement>(nextSelector) : null) ?? nextFocusableAfter(document.querySelector(comboSelector)?.closest('.oe-consrow') ?? null)
    el?.focus()
  }, [comboSelector, nextId, nextSelector])

  return { draft, seedText, begin, cancel, done, reset, clearSeed, tabOut }
}
