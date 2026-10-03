// Lote 8A-app — puente al lector del Zebra (DataWedge, modo intent) para cualquier pantalla que capture un código.
// Sin DataWedge (emulador, teléfono normal, web) no falla: la pantalla sigue funcionando con el teclado (ScanField).
import { useCallback, useEffect, useRef, useState } from 'react'
import { Platform } from 'react-native'

import Datawedge from '../../../modules/datawedge'
import type { ProfileState, ProfileStatus } from '../../../modules/datawedge'

export function useScanner(onScan: (code: string) => void): void {
  const handler = useRef(onScan)
  useEffect(() => {
    handler.current = onScan
  }, [onScan])

  useEffect(() => {
    if (Platform.OS !== 'android') return undefined
    let subscription: { remove(): void } | null = null
    try {
      if (Datawedge.isAvailable()) {
        Datawedge.createProfile()
        subscription = Datawedge.addListener('onScan', (event) => {
          if (event.data) handler.current(event.data)
        })
      }
    } catch {
      // Sin DataWedge disponible: se ignora, la pantalla sigue con teclado.
    }
    return () => subscription?.remove()
  }, [])
}

/** Clave i18n del indicador "Lector: …" de Sincronización (docs/mobile/mejoras-ux-zebra.md §2.3). Nunca dice "listo" sin
 *  que DataWedge lo haya confirmado: sin respuesta es "sin confirmar". */
export function scannerStatusKey(state: ProfileState): string {
  return `scanner.${state}`
}

function readStatus(): ProfileStatus {
  if (Platform.OS !== 'android') return { state: 'unavailable', detail: '' }
  try {
    if (!Datawedge.isAvailable()) return { state: 'unavailable', detail: '' }
    return Datawedge.getProfileStatus()
  } catch {
    // módulo nativo de una versión anterior de la app (sin getProfileStatus): no se sabe
    return { state: 'unconfirmed', detail: '' }
  }
}

/**
 * Estado del perfil del lector, vivo: pregunta a DataWedge al montar y con `recheck()` (que además vuelve a crear el perfil),
 * y se actualiza cuando DataWedge contesta (evento "onProfileStatus").
 */
export function useScannerStatus(): { status: ProfileStatus; recheck: () => void } {
  const [status, setStatus] = useState<ProfileStatus>(readStatus)

  useEffect(() => {
    if (Platform.OS !== 'android') return undefined
    let subscription: { remove(): void } | null = null
    try {
      if (Datawedge.isAvailable()) {
        subscription = Datawedge.addListener('onProfileStatus', (event) => setStatus({ state: event.state, detail: event.detail ?? '' }))
        Datawedge.refreshProfileStatus()
      }
    } catch {
      // sin módulo o sin DataWedge: queda lo que diga readStatus()
    }
    return () => subscription?.remove()
  }, [])

  const recheck = useCallback(() => {
    if (Platform.OS !== 'android') return
    try {
      if (Datawedge.isAvailable()) {
        setStatus({ state: 'unconfirmed', detail: '' })
        Datawedge.createProfile()
      }
    } catch {
      // nada que hacer: el indicador se queda como estaba
    }
  }, [])

  return { status, recheck }
}
