// «Kemi con vida» en el portal: parpadea, mira a los lados, guiña y de vez en cuando hace una mueca; con el puntero encima (web) se queda pendiente y las pupilas lo siguen.
// Las mismas reglas y los mismos tiempos que la web (shared/kemi/rules.ts); aquí solo hay temporizadores con espera al azar que cambian el gesto. No corre con «reducir movimiento».
import { useCallback, useEffect, useRef, useState } from 'react'
import { AccessibilityInfo, Platform, type PointerEvent } from 'react-native'

import { BIG_EVERY, BIG_MOODS, BIG_MS, BLINK_EVERY, BLINK_MS, MICRO_EVERY, MICRO_MOODS, MICRO_MS, between, faceOf, pickMood, pupilOffset, type KemiFace, type KemiMood } from '../../../../shared/kemi/rules'

export interface KemiLife {
  face: KemiFace
  onHoverIn: () => void
  onHoverOut: () => void
  /** Solo en la web: las pupilas siguen al puntero mientras está encima. */
  onPointerMove: (e: PointerEvent) => void
}

/** `enabled` = Kemi despierta, sin globo de aviso y sin panel abierto; `excited` = llegó algo nuevo (ojos grandes y sonrisa). */
export function useKemiLife(enabled: boolean, excited: boolean): KemiLife {
  const [mood, setMood] = useState<KemiMood>('calm')
  const [blink, setBlink] = useState(false)
  const [look, setLook] = useState<{ x: number; y: number } | null>(null)
  const hovering = useRef(false)
  const [reduced, setReduced] = useState(false)

  useEffect(() => {
    let alive = true
    void AccessibilityInfo.isReduceMotionEnabled().then((v) => alive && setReduced(v)).catch(() => undefined)
    return () => {
      alive = false
    }
  }, [])

  useEffect(() => {
    if (!enabled || reduced) return
    const timers = new Set<ReturnType<typeof setTimeout>>()
    const later = (fn: () => void, ms: number) => {
      const id = setTimeout(() => {
        timers.delete(id)
        fn()
      }, ms)
      timers.add(id)
    }
    let last: KemiMood | null = null
    const run = (every: readonly [number, number], act: () => void) => {
      const step = () => {
        if (!hovering.current) act()
        later(step, between(Math.random, every))
      }
      later(step, between(Math.random, every))
    }
    const gesture = (list: readonly KemiMood[], ms: readonly [number, number]) => () => {
      const next = pickMood(Math.random, list, last)
      last = next
      setMood(next)
      later(() => {
        if (!hovering.current) setMood('calm')
      }, between(Math.random, ms))
    }
    run(BLINK_EVERY, () => {
      setBlink(true)
      later(() => setBlink(false), BLINK_MS)
    })
    run(MICRO_EVERY, gesture(MICRO_MOODS, MICRO_MS))
    run(BIG_EVERY, gesture(BIG_MOODS, BIG_MS))
    return () => {
      timers.forEach(clearTimeout)
      setMood('calm')
      setBlink(false)
    }
  }, [enabled, reduced])

  const onHoverIn = useCallback(() => {
    hovering.current = true
    setMood('alert')
  }, [])
  const onHoverOut = useCallback(() => {
    hovering.current = false
    setLook(null)
    setMood('calm')
  }, [])
  const onPointerMove = useCallback((e: PointerEvent) => {
    if (Platform.OS !== 'web' || !hovering.current) return
    try {
      const el = e.currentTarget as unknown as { getBoundingClientRect(): { left: number; top: number; width: number; height: number } }
      const rect = el.getBoundingClientRect()
      const ev = e.nativeEvent as unknown as { clientX: number; clientY: number }
      setLook(pupilOffset(ev.clientX - (rect.left + rect.width / 2), ev.clientY - (rect.top + rect.height / 2)))
    } catch {
      /* sin medidas: se queda mirando de frente */
    }
  }, [])

  return { face: faceOf(mood, blink, excited, look), onHoverIn, onHoverOut, onPointerMove }
}
