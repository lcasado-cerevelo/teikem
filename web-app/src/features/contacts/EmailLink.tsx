// Un correo guardado que se puede tocar para escribir (abre el programa de correo con el destinatario puesto). Misma regla que los teléfonos (PhoneLink).
export function EmailLink({ value, className }: { value: string; className?: string }) {
  const v = value.trim()
  return (
    <a href={`mailto:${v}`} className={className}>
      {v}
    </a>
  )
}
