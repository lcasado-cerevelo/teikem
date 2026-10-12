// Celda «Vehículo» de las tablas de Mantenimiento, Órdenes de trabajo y Combustible (Lote F-D3).
/** Celda «Vehículo» de las tablas (maqueta): la placa y, como etiqueta, el código. */
export function VehicleCell({ plate, code }: { plate?: string | null; code?: string | null }) {
  return (
    <>
      <b className="mono">{plate || code || '—'}</b>
      {plate && code ? <span className="tag flt-tag">{code}</span> : null}
    </>
  )
}

