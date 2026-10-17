// Seguridad y auditoría → Aceptación legal (Lote 99): quién aceptó y quién no la versión vigente del documento «Política de privacidad y términos de uso», entre los usuarios de la compañía.
// Los pendientes salen primero. Tabla paginada por el servidor; exportar baja todo lo que dice el filtro.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, IconShield, Panel, QBox, Spinner, type DataColumn } from '../../kernel/ui'
import { fetchAllAcceptances, useLegalAcceptances, type LegalAcceptanceRowDto } from './legalApi'
import { acceptanceStatusParam, type AcceptanceFilter } from './legalRules'

export function LegalAcceptancesTab() {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState<AcceptanceFilter>('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query = useMemo(() => ({ status: acceptanceStatusParam(status), q: q.trim() || undefined }), [status, q])
  const list = useLegalAcceptances(query, (page - 1) * pageSize, pageSize)

  const columns = useMemo<DataColumn<LegalAcceptanceRowDto>[]>(
    () => [
      { id: 'name', header: t('legal.list.user'), cell: (r) => r.fullName ?? r.email ?? '—', sortValue: (r) => r.fullName ?? r.email ?? '', exportValue: (r) => r.fullName ?? '', card: 'title' },
      { id: 'email', header: t('legal.list.email'), cell: (r) => r.email ?? '—', sortValue: (r) => r.email ?? '', exportValue: (r) => r.email ?? '' },
      {
        id: 'state',
        header: t('legal.list.state'),
        cell: (r) => <Chip tone={r.accepted ? 'deliv' : 'warn'}>{r.accepted ? t('legal.list.accepted') : r.lastVersion ? t('legal.list.oldVersion', { v: r.lastVersion }) : t('legal.list.pending')}</Chip>,
        sortValue: (r) => (r.accepted ? 1 : 0),
        exportValue: (r) => (r.accepted ? t('legal.list.accepted') : t('legal.list.pending')),
      },
      { id: 'when', header: t('legal.list.when'), cell: (r) => (r.acceptedAtUtc ? f.dateTime(r.acceptedAtUtc) : '—'), sortValue: (r) => r.acceptedAtUtc ?? '', exportValue: (r) => r.acceptedAtUtc ?? '' },
    ],
    [t, f],
  )

  return (
    <Panel icon={<IconShield />} title={t('legal.list.title')}>
      <div className="pb">
        <p className="help">{t('legal.list.help')}</p>
        <div className="filters" role="group" aria-label={t('legal.list.filters')}>
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('legal.list.search')} />
          <select aria-label={t('legal.list.state')} value={status} onChange={(e) => (setStatus(e.target.value as AcceptanceFilter), setPage(1))}>
            <option value="">{t('legal.list.any')}</option>
            <option value="pending">{t('legal.list.pending')}</option>
            <option value="accepted">{t('legal.list.accepted')}</option>
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('legal.list.title')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(r) => r.email ?? r.fullName ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            empty={t('legal.list.empty')}
            exportRows={() => fetchAllAcceptances(query)}
          />
        )}
      </div>
    </Panel>
  )
}
