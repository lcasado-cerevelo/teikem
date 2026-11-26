// Lote 248: panel «Impuestos» de la ficha del cliente (tax.manage, módulo TAX): las exenciones de IVU del cliente con el documento que las respalda. «Nueva exención» y «Renovar»
// abren la captura única (certificado + exención en un paso). Una exención vieja sin documento sigue valiendo igual y sale marcada «Falta el documento».
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Panel, Spinner, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconRotateCcw } from '../../kernel/ui/actionIcons'
import { IconCapitol } from '../../kernel/ui/screenIcons'
import { ExemptionCaptureModal } from '../tax/ExemptionCaptureModal'
import { componentNames, scopeOf } from '../tax/taxRules'
import { useComponentOptions } from '../tax/useComponentOptions'
import { useClientExemptions, type TaxExemptionDto } from './documentsApi'
import type { ClientDetail } from './clientRules'

const STATUS_TONE: Record<string, ChipTone> = { VALID: 'deliv', SCHEDULED: 'warn', EXPIRED: 'fail', INACTIVE: 'neutral' }

export function ClientTaxPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = client.publicId ?? ''
  const list = useClientExemptions(publicId)
  const options = useComponentOptions()
  const [modal, setModal] = useState<TaxExemptionDto | 'new' | null>(null)
  const rows = list.data ?? []
  const valid = rows.filter((e) => e.status === 'VALID').length

  const scopeText = (e: TaxExemptionDto) => (scopeOf(e.scope) === 'COMPONENTS' ? t('tax.exemptions.scopeOnly', { names: componentNames(e.componentCodes, options) }) : t('tax.exemptions.scopes.ALL'))
  const columns = useMemo<DataColumn<TaxExemptionDto>[]>(
    () => [
      {
        id: 'type',
        header: t('tax.exemptions.type'),
        cell: (e) => (
          <span className="cdoc-type">
            <b>{e.documentTypeLabel ?? e.exemptionType}</b>
            {e.missingDocument && <Chip tone="warn" title={t('tax.exemptions.missingDocumentHelp')}>{t('tax.exemptions.missingDocument')}</Chip>}
          </span>
        ),
        sortValue: (e) => e.documentTypeLabel ?? e.exemptionType ?? '',
        exportValue: (e) => e.documentTypeLabel ?? e.exemptionType ?? '',
        card: 'title',
      },
      { id: 'cert', header: t('tax.exemptions.certificate'), cell: (e) => e.certificateNumber ?? '—', sortValue: (e) => e.certificateNumber ?? '' },
      { id: 'scope', header: t('tax.exemptions.scope'), cell: scopeText, sortValue: scopeText, exportValue: scopeText },
      { id: 'from', header: t('tax.exemptions.from'), cell: (e) => f.date(e.validFrom), sortValue: (e) => e.validFrom ?? '' },
      { id: 'to', header: t('tax.exemptions.to'), cell: (e) => (e.validTo ? f.date(e.validTo) : t('tax.rates.open')), sortValue: (e) => e.validTo ?? '9999-12-31' },
      { id: 'status', header: t('tax.exemptions.status'), cell: (e) => <Chip tone={STATUS_TONE[e.status ?? 'INACTIVE']}>{t(`tax.exemptions.statuses.${e.status}`)}</Chip>, sortValue: (e) => e.status ?? '' },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, options],
  )

  return (
    <Panel
      icon={<IconCapitol />}
      title={t('clients.tax.title')}
      collapsible
      defaultCollapsed
      summary={list.isPending ? undefined : t('clients.tax.summary', { n: valid })}
      actions={<button type="button" className="btn sm flow" onClick={() => setModal('new')}>{t('clients.tax.new')}</button>}
    >
      <div className="pb">
        <p className="help">{t('clients.tax.help')}</p>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('clients.tax.title')}
            columns={columns}
            rows={rows}
            rowKey={(e) => String(e.id)}
            pagination={false}
            exportable={false}
            dense
            defaultSort={{ id: 'from', desc: true }}
            empty={t('clients.tax.empty')}
            rowActions={[{ key: 'renew', label: t('clients.tax.renew'), icon: <IconRotateCcw />, onClick: (e) => setModal(e) }]}
          />
        )}
      </div>
      {modal && (
        <ExemptionCaptureModal
          key={modal === 'new' ? 'new' : modal.id}
          clientPublicId={publicId}
          clientName={client.name}
          renewScope={modal === 'new' ? null : (modal.scope ?? 'ALL')}
          renewComponents={modal === 'new' ? null : (modal.componentCodes ?? [])}
          onClose={() => setModal(null)}
        />
      )}
    </Panel>
  )
}
