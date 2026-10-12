// Lote F-F2 — «Vistas e informes» (`/analytics/reports`, `analytics.view` + ANALYTICS; escribir: `analytics.manage`). Sin `?view=` es la
// galería por fuente de datos; con `?view=<id>` (patrón de Rentas) es el resultado de esa vista. Referencia: `views()` de la maqueta.
import { useSearchParams } from 'react-router-dom'
import { ReportGallery } from './ReportGallery'
import { ReportResultScreen } from './ReportResultScreen'
import './reports.css'

export default function ReportsScreen() {
  const [params] = useSearchParams()
  const view = Number(params.get('view'))
  return <div className="wrap">{Number.isInteger(view) && view > 0 ? <ReportResultScreen id={view} /> : <ReportGallery />}</div>
}
