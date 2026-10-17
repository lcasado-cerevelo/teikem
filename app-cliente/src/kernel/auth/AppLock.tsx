// Bloqueo con huella de la app: si «Abrir con huella» está activo, la app se bloquea al arrancar con una sesión guardada y al volver de segundo plano pasados LOCK_AFTER_MINUTES (lockRules.ts). Lo lee la raíz para mandar a /lock.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { AppState } from 'react-native'

import { shouldLock } from './lockRules'
import { getBiometricEnabled, setBiometricEnabled } from './preferences'
import { useSessionState } from './session'

interface AppLock {
  /** «Abrir con huella» activo. */
  enabled: boolean
  /** La app está bloqueada y espera la huella. */
  locked: boolean
  setEnabled: (value: boolean) => Promise<void>
  unlock: () => void
}

const Ctx = createContext<AppLock>({ enabled: false, locked: false, setEnabled: async () => undefined, unlock: () => undefined })

export function useAppLock(): AppLock {
  return useContext(Ctx)
}

export function AppLockProvider({ children }: { children: ReactNode }) {
  const { loaded, session } = useSessionState()
  const [enabled, setEnabledState] = useState(false)
  const [locked, setLocked] = useState(false)
  const backgroundedAt = useRef<number | null>(null)
  const enabledRef = useRef(false)
  const startChecked = useRef(false)

  // al arrancar con una sesión guardada: si «Abrir con huella» estaba activo, arranca bloqueada
  useEffect(() => {
    if (!loaded || startChecked.current) return
    startChecked.current = true
    void getBiometricEnabled().then((on) => {
      enabledRef.current = on
      setEnabledState(on)
      if (on && session) setLocked(true)
    })
  }, [loaded, session])

  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'background') backgroundedAt.current = Date.now()
      else if (state === 'active') {
        if (shouldLock(enabledRef.current, backgroundedAt.current, Date.now())) setLocked(true)
        backgroundedAt.current = null
      }
    })
    return () => sub.remove()
  }, [])

  // sin sesión no hay nada que bloquear
  useEffect(() => {
    if (loaded && !session) setLocked(false)
  }, [loaded, session])

  const setEnabled = useCallback(async (value: boolean) => {
    await setBiometricEnabled(value)
    enabledRef.current = value
    setEnabledState(value)
    if (!value) setLocked(false)
  }, [])

  const unlock = useCallback(() => setLocked(false), [])
  const value = useMemo(() => ({ enabled, locked, setEnabled, unlock }), [enabled, locked, setEnabled, unlock])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
