// Un texto donde los teléfonos, correos y direcciones web se pueden tocar (en un celular el teléfono abre el marcador con el número puesto; en una computadora el
// sistema decide qué aplicación atiende el enlace). El resto del texto no cambia.
import { splitLinks } from './links'

export function LinkText({ children }: { children: string }) {
  return (
    <>
      {splitLinks(children).map((p, i) =>
        p.type === 'text' ? (
          p.text
        ) : (
          <a key={i} href={p.href} className="msg-link" {...(p.type === 'url' ? { target: '_blank', rel: 'noreferrer noopener' } : {})}>
            {p.text}
          </a>
        ),
      )}
    </>
  )
}
