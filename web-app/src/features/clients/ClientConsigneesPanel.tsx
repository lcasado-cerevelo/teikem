// Panel «Consignatarios» de la ficha del cliente (va al final, antes del historial de estatus): los consignatarios que son de
// este cliente en una tabla paginada ahí mismo (10 filas por página), con buscador simple y tres acciones —Exportar (lo filtrado,
// todas las filas, no solo la página), Importar y Nuevo consignatario—. Es desplegable: cerrado dice cuántos hay. «Importar» y «Nuevo» abren las mismas ventanas de la
// pantalla Consignatarios con este cliente como dueño; al cerrarlas se está todavía en la ficha del cliente y la tabla se
// refresca sola (las escrituras de consignatarios invalidan sus listas). Una fila abre el consignatario en su pantalla.
import { useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, type DataColumn, EmptyState, FilterScope, IconPlus, IconUpload, Panel, QBox, Spinner, useElementWidth } from '../../kernel/ui'
import { ExportMenu } from '../../kernel/ui/ExportMenu'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { useConsignees } from '../consignees/api'
import { exportConsignees } from '../consignees/consigneeExport'
import { ConsigneeFormModal } from '../consignees/ConsigneeFormModal'
import { ConsigneeImportModal } from '../consignees/ConsigneeImportModal'
import { matchesConsignee, type Consignee } from '../consignees/consigneeRules'
import type { ClientDetail } from './clientRules'

/** Filas por página de la tabla (el contador y la exportación cuentan todas las filtradas). */
export const CLIENT_CONSIGNEES_ROWS = 10

const NO_ROWS: Consignee[] = []

/** Sin el permiso de leer consignatarios el panel no se pinta (la lista del API respondería 403). */
export function ClientConsigneesPanel({ client }: { client: ClientDetail }) {
  const canRead = useCan('locations.read')
  return canRead ? <ClientConsigneesTable client={client} /> : null
}

function ClientConsigneesTable({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const clientId = client.publicId ?? ''
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const [importing, setImporting] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  // solo los consignatarios de este cliente (sin los compartidos) y solo los activos, como la lista de Consignatarios por omisión
  const list = useConsignees(clientId, false)

  const labels = useMemo(() => ({ shared: t('consignees.shared'), active: t('consignees.active'), inactive: t('consignees.inactive') }), [t])
  const filtered = useMemo(() => (list.data ?? NO_ROWS).filter((c) => matchesConsignee(c, q, labels)), [list.data, q, labels])

  const columns = useMemo<DataColumn<Consignee>[]>(
    () => [
      { id: 'code', header: t('consignees.number'), cell: (c) => (c.code ? <span className="ref">{c.code}</span> : '—'), sortValue: (c) => c.code, card: 'hidden' },
      { id: 'name', header: t('consignees.name'), cell: (c) => <b>{c.name}</b>, sortValue: (c) => c.name, card: 'title' },
      { id: 'town', header: t('consignees.town'), cell: (c) => c.city || '—', sortValue: (c) => c.city },
      { id: 'contact', header: t('consignees.contactName'), cell: (c) => c.contactName || '—', sortValue: (c) => c.contactName },
      {
        id: 'status',
        header: t('consignees.status'),
        cell: (c) => <Chip tone={c.isActive ? 'deliv' : 'cap'}>{c.isActive ? t('consignees.active') : t('consignees.inactive')}</Chip>,
        sortValue: (c) => (c.isActive ? 0 : 1),
      },
    ],
    [t],
  )

  const openRow = (c: Consignee) => {
    if (c.publicId) navigate(`/catalog/consignees?consignee=${encodeURIComponent(c.publicId)}`)
  }

  return (
    <Panel
      flush
      icon={<IconUsers />}
      title={t('clients.consignees.title')}
      badge={list.isPending ? undefined : filtered.length}
      collapsible
      defaultCollapsed
      summary={list.isPending ? t('common.loading') : (list.data ?? NO_ROWS).length === 0 ? t('clients.summary.empty') : t('clients.summary.consignees', { n: (list.data ?? NO_ROWS).length })}
      actions={
        <>
          <ExportMenu placement="down" count={filtered.length} onExport={(format) => exportConsignees(format, filtered, t, f.lang)} />
          <Can perm="locations.create">
            <button type="button" className="btn sm" onClick={() => setImporting(true)}>
              <IconUpload /> {t('consignees.importBtn')}
            </button>
            <button type="button" className="btn sm flow" onClick={() => setCreating(true)}>
              <IconPlus /> {t('consignees.new')}
            </button>
          </Can>
        </>
      }
    >
      <div ref={ref}>
        <FilterScope>
          <div className="qrow cl-q">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
          </div>
        </FilterScope>
        {list.isError ? (
          <p className="pb ferr" role="alert">
            {applyProblemDetails(list.error).title}
          </p>
        ) : list.isPending ? (
          <Spinner block />
        ) : filtered.length === 0 ? (
          <EmptyState icon={<IconUsers />} title={q ? t('clients.consignees.noResults') : t('clients.consignees.empty')} />
        ) : (
          <DataTable
            label={t('clients.consignees.title')}
            columns={columns}
            rows={filtered}
            rowKey={(c) => c.publicId ?? String(c.id)}
            pageSize={CLIENT_CONSIGNEES_ROWS}
            exportable={false}
            onRowClick={openRow}
            forceCards={width > 0 && width < 640}
          />
        )}
      </div>

      <ConsigneeFormModal
        key={creating ? `new-${clientId}` : 'closed'}
        open={creating}
        consignee={null}
        phone={undefined}
        defaultOwner={clientId}
        onClose={() => setCreating(false)}
        onSaved={() => setQ('')}
      />
      {importing && <ConsigneeImportModal defaultClient={clientId} onClose={() => setImporting(false)} />}
    </Panel>
  )
}
