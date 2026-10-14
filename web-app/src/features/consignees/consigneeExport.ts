// Exportar consignatarios (Excel / CSV / PDF) — exactamente las filas que la pantalla tiene a la vista: la lista de
// Consignatarios con su filtro de cliente y su buscador, o el panel «Consignatarios» de la ficha del cliente con su buscador
// (todas las filas filtradas, no solo las 10 que muestra la tabla). Mismo motor que DataTable (`exportTable`): valores
// escapados, protección contra fórmulas y nombre de archivo con la fecha.
import type { ExportableColumn, ExportFormat } from '../../kernel/ui/exportTable'
import { exportTable } from '../../kernel/ui/exportTable'
import { ownerLabel, type Consignee } from './consigneeRules'

type Translate = (key: string, vars?: Record<string, string | number>) => string

/** Columnas del archivo: lo que la pantalla muestra más la dirección completa y el contacto (sin teléfono: vive aparte). */
export function consigneeExportColumns(t: Translate): ExportableColumn<Consignee>[] {
  const text = (v: string | null | undefined) => v ?? ''
  return [
    { header: t('consignees.export.code'), cell: (c) => text(c.code) },
    { header: t('consignees.export.name'), cell: (c) => text(c.name) },
    { header: t('consignees.export.client'), cell: (c) => ownerLabel(c, t('consignees.shared')) },
    { header: t('consignees.export.line1'), cell: (c) => text(c.line1) },
    { header: t('consignees.export.line2'), cell: (c) => text(c.line2) },
    { header: t('consignees.export.city'), cell: (c) => text(c.city) },
    { header: t('consignees.export.state'), cell: (c) => text(c.state) },
    { header: t('consignees.export.postalCode'), cell: (c) => text(c.postalCode) },
    { header: t('consignees.export.country'), cell: (c) => text(c.countryLabel ?? c.country) },
    { header: t('consignees.export.contact'), cell: (c) => text(c.contactName) },
    { header: t('consignees.export.status'), cell: (c) => (c.isActive ? t('consignees.active') : t('consignees.inactive')) },
  ]
}

/** Descarga las filas dadas en el formato pedido. Lanza si falla (el menú de exportar avisa con un toast). */
export async function exportConsignees(format: ExportFormat, rows: readonly Consignee[], t: Translate, locale?: string): Promise<void> {
  await exportTable(format, consigneeExportColumns(t), rows, { title: t('consignees.export.fileName'), locale })
}
