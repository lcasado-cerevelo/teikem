// Contextos internos de Form/Field: el campo actual (nombre, id, error) y el registro de nombres que pinta el
// formulario (para saber qué errores del servidor no tienen campo y mostrarlos arriba).
import { createContext, useContext } from 'react'

export interface FieldInfo {
  name: string
  id: string
  invalid: boolean
  required: boolean
  describedBy?: string
}

export const FieldContext = createContext<FieldInfo | null>(null)

/** Datos del <Field> que envuelve al control. Los inputs del kit lo usan para registrarse. */
export function useFieldInfo(component: string): FieldInfo {
  const info = useContext(FieldContext)
  if (!info) throw new Error(`${component} debe ir dentro de <Field name="…">`)
  return info
}

export interface FieldRegistry {
  add: (name: string) => void
  remove: (name: string) => void
}

export const FieldRegistryContext = createContext<FieldRegistry | null>(null)
