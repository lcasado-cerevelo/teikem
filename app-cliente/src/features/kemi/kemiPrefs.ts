// Lo que el portal recuerda de Kemi por persona (Lote 218): si la durmió y dónde la dejó. Es una comodidad de ESTE aparato o navegador (como en la web: localStorage), no se guarda en el servidor.
// Usa el mismo almacén de la app (seguro en el teléfono, localStorage en la web); si no hay donde guardar, vale mientras la pantalla siga abierta.
import { useCallback, useEffect, useRef, useState } from 'react'

import { DEFAULT_POS, isMoved, type KemiPos } from '../../../../shared/kemi/rules'
import { storeDelete, storeGet, storeSet } from '../../kernel/storage'

const ASLEEP = 'teikem.kemi.asleep'
const POS = 'teikem.kemi.pos'

/** Una posición guardada que no sirve (datos dañados) es la esquina de siempre. */
export function parsePos(raw: string | null): KemiPos {
  try {
    const v = raw ? (JSON.parse(raw) as Partial<KemiPos>) : null
    if (v && typeof v.right === 'number' && typeof v.bottom === 'number' && Number.isFinite(v.right) && Number.isFinite(v.bottom)) return { right: v.right, bottom: v.bottom }
  } catch {
    /* datos dañados */
  }
  return DEFAULT_POS
}

export interface KemiPrefs {
  ready: boolean
  asleep: boolean
  setAsleep: (v: boolean) => void
  pos: KemiPos
  setPos: (p: KemiPos) => void
}

export function useKemiPrefs(userKey: string | null): KemiPrefs {
  const [ready, setReady] = useState(false)
  const [asleep, setAsleepState] = useState(false)
  const [pos, setPosState] = useState<KemiPos>(DEFAULT_POS)
  const key = useRef(userKey)

  useEffect(() => {
    key.current = userKey
    let alive = true
    setReady(false)
    void (async () => {
      const [a, p] = await Promise.all([storeGet(`${ASLEEP}.${userKey ?? ''}`), storeGet(`${POS}.${userKey ?? ''}`)])
      if (!alive) return
      setAsleepState(a === '1')
      setPosState(parsePos(p))
      setReady(true)
    })()
    return () => {
      alive = false
    }
  }, [userKey])

  const setAsleep = useCallback((v: boolean) => {
    setAsleepState(v)
    const k = `${ASLEEP}.${key.current ?? ''}`
    void (v ? storeSet(k, '1') : storeDelete(k))
  }, [])
  const setPos = useCallback((p: KemiPos) => {
    setPosState(p)
    const k = `${POS}.${key.current ?? ''}`
    void (isMoved(p) ? storeSet(k, JSON.stringify(p)) : storeDelete(k))
  }, [])

  return { ready, asleep, setAsleep, pos, setPos }
}
