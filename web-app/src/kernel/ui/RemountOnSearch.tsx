// Vuelve a montar su contenido cuando cambia la parte `?…` de la dirección. Lo usan las listas que leen un filtro de la dirección al abrirse
// (`?q=` de la búsqueda global): si ya estaban abiertas y se elige otro resultado, se abren de nuevo con el filtro nuevo.
import { Fragment, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'

export function RemountOnSearch({ children }: { children: ReactNode }) {
  const { search } = useLocation()
  return <Fragment key={search}>{children}</Fragment>
}
