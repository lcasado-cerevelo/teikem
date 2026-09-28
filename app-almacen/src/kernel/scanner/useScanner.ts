// Lote 8A-app — puente al lector del Zebra (DataWedge, modo intent) para cualquier pantalla que capture un código.
// Sin DataWedge (emulador, teléfono normal, web) no falla: la pantalla sigue funcionando con el teclado (ScanField).
import { useEffect, useRef } from 'react'
import { Platform } from 'react-native'

import Datawedge from '../../../modules/datawedge'

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
