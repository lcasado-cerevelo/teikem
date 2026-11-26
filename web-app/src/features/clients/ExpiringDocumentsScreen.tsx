// Catálogo → Documentos por vencer (Lote 248, clients.read, módulo CATALOG): los documentos de los clientes cuyo «hasta» cae dentro de la ventana (por omisión, el primer aviso de la compañía),
// los ya vencidos y los de un tipo con vigencia sin «hasta». No trae los renovados. Las exenciones de IVU viejas «falta el documento» salen solo con tax.manage. El servidor manda hasta 2000 filas
// (no pagina): aquí se busca y se ordena. «Abrir» lleva a la ficha del cliente, en Documentos.
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, matchesQ, Panel, QBox, Spinner, type DataColumn } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { expiryStateKey, expiryTone } from './documentRules'
import { useExpiringDocuments, type ExpiringClientDocumentDto } from './documentsApi'
import './clients.css'

const NO_ROWS: ExpiringClientDocumentDto[] = []
const WINDOWS = ['', '15', '30', '60', '90', '180', '365'] as const

export default function ExpiringDocumentsScreen() {
  const t = useT()
  const f = useFormat()
  const [within, setWithin] = useState<string>('')
  const [includeExpired, setIncludeExpired] = useState(true)
  const [includeMissing, setIncludeMissing] = useState(true)
  const [q, setQ] = useState('')
  const list = useExpiringDocuments({ withinDays: within ? Number(within) : undefined, includeExpired, includeMissing })
  const rows = useMemo(
    () => (list.data?.items ?? NO_ROWS).filter((r) => matchesQ(q, r.clientCode, r.clientName, r.documentTypeLabel, r.documentType, r.documentNumber)),
    [list.data, q],
  )
  const days = (n: number | null | undefined) => (n == null ? '—' : n === 0 ? t('clients.documents.today') : n > 0 ? t('clients.documents.inDays', { n }) : t('clients.documents.daysAgo', { n: -n }))
  const stateText = (r: ExpiringClientDocumentDto) => (r.rowKind === 'EXEMPTION_WITHOUT_DOCUMENT' ? t('tax.exemptions.missingDocument') : t(expiryStateKey(r.expiryState)))

  const columns = useMemo<DataColumn<ExpiringClientDocumentDto>[]>(
    () => [
      {
        id: 'client',
        header: t('clients.expiring.client'),
        cell: (r) => (
          <Link className="dexp-client" to={`/catalog/clients?client=${r.clientPublicId}`}>
            <b>{r.clientCode}</b> {r.clientName}
          </Link>
        ),
        sortValue: (r) => r.clientName ?? '',
        exportValue: (r) => [r.clientCode, r.clientName].filter(Boolean).join(' · '),
        card: 'title',
      },
      {
        id: 'type',
        header: t('clients.documents.type'),
        cell: (r) => (
          <span className="cdoc-type">
            {r.rowKind === 'EXEMPTION_WITHOUT_DOCUMENT' ? t('clients.expiring.exemptionRow') : (r.documentTypeLabel ?? r.documentType)}
            {r.backsExemption && <Chip tone="cod" title={t('clients.documents.backsHelp')}>{t('clients.documents.backs')}</Chip>}
          </span>
        ),
        sortValue: (r) => r.documentTypeLabel ?? r.documentType ?? '',
        exportValue: (r) => r.documentTypeLabel ?? r.documentType ?? '',
      },
      { id: 'number', header: t('clients.documents.number'), cell: (r) => r.documentNumber ?? '—', sortValue: (r) => r.documentNumber ?? '', exportValue: (r) => r.documentNumber ?? '' },
      { id: 'from', header: t('clients.documents.from'), cell: (r) => (r.validFrom ? f.date(r.validFrom) : '—'), sortValue: (r) => r.validFrom ?? '', exportValue: (r) => r.validFrom ?? '' },
      { id: 'to', header: t('clients.documents.to'), cell: (r) => (r.validTo ? f.date(r.validTo) : '—'), sortValue: (r) => r.validTo ?? '9999-12-31', exportValue: (r) => r.validTo ?? '' },
      { id: 'days', header: t('clients.documents.days'), cell: (r) => days(r.daysToExpiry), sortValue: (r) => r.daysToExpiry ?? 999_999, exportValue: (r) => r.daysToExpiry ?? '', align: 'end' },
      {
        id: 'state',
        header: t('clients.documents.state'),
        cell: (r) => <Chip tone={r.rowKind === 'EXEMPTION_WITHOUT_DOCUMENT' ? 'warn' : expiryTone(r.expiryState)}>{stateText(r)}</Chip>,
        sortValue: stateText,
        exportValue: stateText,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f],
  )

  return (
    <div className="wrap dexp">
      <div className="head">
        <div>
          <h1>{t('clients.expiring.title')}</h1>
          <p>{t('clients.expiring.subtitle')}</p>
        </div>
      </div>
      <Panel flush icon={<IconDoc />} title={t('clients.expiring.list')} badge={list.isPending ? undefined : rows.length}>
        <div className="qrow dexp-q">
          <QBox value={q} onChange={setQ} placeholder={t('clients.expiring.search')} />
          <div className="f dexp-within">
            <label htmlFor="dexp-within">{t('clients.expiring.within')}</label>
            <select id="dexp-within" value={within} onChange={(e) => setWithin(e.target.value)}>
              {WINDOWS.map((w) => (
                <option key={w} value={w}>{w ? t('clients.expiring.withinDays', { n: Number(w) }) : t('clients.expiring.withinDefault', { n: list.data?.warnDays ?? 30 })}</option>
              ))}
            </select>
          </div>
          <label className="sw">
            <input type="checkbox" role="switch" checked={includeExpired} onChange={(e) => setIncludeExpired(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('clients.expiring.includeExpired')}</span>
          </label>
          <label className="sw">
            <input type="checkbox" role="switch" checked={includeMissing} onChange={(e) => setIncludeMissing(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('clients.expiring.includeMissing')}</span>
          </label>
        </div>
        {list.isError ? (
          <p className="pb ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <>
            {list.data?.truncated && <p className="pb help" role="status">{t('clients.expiring.truncated')}</p>}
            <DataTable
              label={t('clients.expiring.list')}
              columns={columns}
              rows={rows}
              rowKey={(r) => r.documentPublicId ?? r.exemptionPublicId ?? ''}
              defaultSort={{ id: 'to', desc: false }}
              dense
              empty={q ? t('clients.expiring.noResults') : t('clients.expiring.empty')}
            />
          </>
        )}
      </Panel>
    </div>
  )
}
