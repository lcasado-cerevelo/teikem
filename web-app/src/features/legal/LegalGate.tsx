// Compuerta del documento legal (Lote 99): con sesión y sin aceptar la versión vigente, en lugar de la aplicación sale la pantalla de aceptación. Si el estado no se puede leer, se deja pasar
// (el servidor sigue exigiéndolo con 403 'legal_acceptance_required').
import { useRef, type ReactNode } from 'react'
import { Splash } from '../../app/Splash'
import { LegalAcceptPage } from './LegalAcceptPage'
import { useLegalStatus } from './legalApi'

export function LegalGate({ children }: { children: ReactNode }) {
  const status = useLegalStatus(true)
  // solo la primera lectura muestra la carga: un refresco posterior (p. ej. al cambiar de idioma) no desmonta la aplicación
  const settled = useRef(false)
  if (!status.isPending) settled.current = true
  if (status.isPending && !settled.current) return <Splash full />
  if (status.data?.required && !status.data.accepted) return <LegalAcceptPage />
  return <>{children}</>
}
