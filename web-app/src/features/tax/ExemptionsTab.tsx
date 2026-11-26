// Impuestos → Exenciones (Lote 56): certificados de exención de clientes. Con una exención vigente nada se le cobra de impuesto y la línea
// queda con el certificado anotado; con la exención vencida se cobra y se avisa. Nunca se borran: se dan de baja y se pueden reactivar.
// Lote 248: toda exención se respalda con un documento del cliente (número y vigencia son los del documento): «Nueva exención» es la captura única (certificado + exención);
// las exenciones viejas sin documento siguen valiendo igual y se marcan «Falta el documento» hasta que se les vincule uno.
// Lote 237: «Aplica a» — todo el impuesto (como siempre) o solo algunos componentes de la tasa (p. ej. la exención municipal del IVU: se deja de
// cobrar el municipal y el estatal se cobra). Las casillas son los componentes de la tasa por omisión vigente.
import { useId, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, type ChipTone, type DataColumn, DataTable, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconCheckCircle, IconEdit, IconPower } from '../../kernel/ui/actionIcons'
import { useClientDocuments } from '../clients/clientSalesApi'
import { ExemptionCaptureModal } from './ExemptionCaptureModal'
import { useComponentOptions } from './useComponentOptions'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { componentNames, scopeBody, scopeOf, type ExemptionDraft, type ExemptionScope } from './taxRules'
import { useTaxExemptions, useTaxMutations, type TaxExemptionDto } from './taxApi'

const STATUS_TONE: Record<string, ChipTone> = { VALID: 'deliv', SCHEDULED: 'warn', EXPIRED: 'fail', INACTIVE: 'neutral' }
const NO_ROWS: TaxExemptionDto[] = []
const NO_CODES: string[] = []
const SCOPES: ExemptionScope[] = ['ALL', 'COMPONENTS']

/** Editar una exención con documento: el número y la vigencia son los del documento (solo lectura); se editan el tipo, el alcance y las notas. */
function ExemptionModal({ exemption, onClose }: { exemption: TaxExemptionDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const scopeName = useId()
  const { updateExemption } = useTaxMutations()
  const options = useComponentOptions(exemption.componentCodes ?? NO_CODES)
  const [draft, setDraft] = useState<ExemptionDraft>({
    clientId: exemption.clientId ?? null,
    type: exemption.exemptionType ?? '',
    certificate: exemption.certificateNumber ?? '',
    from: exemption.validFrom ?? '',
    to: exemption.validTo ?? '',
    notes: exemption.notes ?? '',
    scope: scopeOf(exemption.scope),
    components: [...(exemption.componentCodes ?? [])],
  })
  const toggleComponent = (code: string, on: boolean) => setDraft((d) => ({ ...d, components: on ? [...d.components.filter((c) => c !== code), code] : d.components.filter((c) => c !== code) }))
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<ExemptionDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const busy = updateExemption.isPending

  const save = async () => {
    setError(null)
    if (!draft.type.trim()) return setError(t('tax.exemptions.errors.type'))
    if (draft.scope === 'COMPONENTS' && draft.components.length === 0) return setError(t('tax.exemptions.errors.components'))
    try {
      // Lote 248: con documento, número y fechas son los del documento; mandarlos da 400
      await updateExemption.mutateAsync({ id: exemption.id!, body: { exemptionType: draft.type, notes: draft.notes, ...scopeBody(draft) } })
      toast.success(t('tax.exemptions.updated'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('tax.exemptions.edit')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>{busy ? t('common.loading') : t('tax.exemptions.save')}</button>
        </>
      }
    >
      <div className="pb tax-form">
        <div className="f">
          <label htmlFor="tx-client">{t('tax.exemptions.client')}</label>
          <input id="tx-client" value={exemption.clientName ?? ''} readOnly />
        </div>
        <div className="f">
          <label htmlFor="tx-etype">{t('tax.exemptions.type')}</label>
          <input id="tx-etype" value={draft.type} maxLength={60} autoComplete="off" onChange={(e) => set({ type: e.target.value })} />
        </div>
        <div className="txc-doc" role="group" aria-label={t('tax.capture.document')}>
          <b>{exemption.documentTypeLabel ?? exemption.documentType}</b>
          <span>{[exemption.certificateNumber, exemption.validFrom ? f.date(exemption.validFrom) : null, exemption.validTo ? f.date(exemption.validTo) : t('tax.rates.open')].filter(Boolean).join(' · ')}</span>
          <span className="help">{t('tax.capture.documentHelp')}</span>
        </div>
        <fieldset className="tax-comps tax-scope">
          <legend>{t('tax.exemptions.scope')}</legend>
          <div className="tax-scope-options" role="radiogroup" aria-label={t('tax.exemptions.scope')}>
            {SCOPES.map((s) => (
              <label key={s} className="tax-check">
                <input type="radio" name={scopeName} value={s} checked={draft.scope === s} onChange={() => set({ scope: s })} />
                <span>{t(`tax.exemptions.scopes.${s}`)}</span>
              </label>
            ))}
          </div>
          {draft.scope === 'COMPONENTS' ? (
            options.length === 0 ? (
              <p className="help" role="status">{t('tax.exemptions.noComponents')}</p>
            ) : (
              <div className="tax-scope-options" role="group" aria-label={t('tax.exemptions.components')}>
                {options.map((o) => (
                  <label key={o.code} className="tax-check">
                    <input type="checkbox" checked={draft.components.includes(o.code)} onChange={(e) => toggleComponent(o.code, e.target.checked)} />
                    <span>{o.percent === null ? t('tax.exemptions.componentNotInRate', { name: o.name }) : t('tax.exemptions.componentOption', { name: o.name, pct: f.number(o.percent, { maximumFractionDigits: 4 }) })}</span>
                  </label>
                ))}
              </div>
            )
          ) : null}
          <p className="help">{t(draft.scope === 'COMPONENTS' ? 'tax.exemptions.scopeHelpComponents' : 'tax.exemptions.scopeHelpAll')}</p>
        </fieldset>
        <div className="f">
          <label htmlFor="tx-notes">{t('tax.exemptions.notes')}</label>
          <textarea id="tx-notes" rows={3} maxLength={300} value={draft.notes} onChange={(e) => set({ notes: e.target.value })} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

/** Vincular a una exención vieja («falta el documento») un documento ya subido del mismo cliente y de un tipo de impuestos. */
function LinkDocumentModal({ exemption, onClose }: { exemption: TaxExemptionDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { updateExemption } = useTaxMutations()
  const docs = useClientDocuments(exemption.clientPublicId ?? '', !!exemption.clientPublicId)
  const [selected, setSelected] = useState('')
  const [error, setError] = useState<string | null>(null)
  const candidates = (docs.data ?? []).filter((d) => d.isTax)
  const busy = updateExemption.isPending

  const save = async () => {
    setError(null)
    if (!selected) return setError(t('tax.link.errors.pick'))
    try {
      await updateExemption.mutateAsync({ id: exemption.id!, body: { clientDocumentPublicId: selected } })
      toast.success(t('tax.link.done'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('tax.link.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy || docs.isPending}>{busy ? t('common.loading') : t('tax.link.save')}</button>
        </>
      }
    >
      <div className="pb tax-form">
        <p>{t('tax.link.intro', { client: exemption.clientName ?? '' })}</p>
        {docs.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(docs.error).title}</p>
        ) : docs.isPending ? (
          <Spinner block />
        ) : candidates.length === 0 ? (
          <p className="help" role="status">{t('tax.link.none')}</p>
        ) : (
          <div className="f">
            <label htmlFor="tx-link-doc">{t('tax.link.document')}</label>
            <select id="tx-link-doc" value={selected} onChange={(e) => setSelected(e.target.value)}>
              <option value="">{t('tax.link.placeholder')}</option>
              {candidates.map((d) => (
                <option key={d.publicId} value={d.publicId}>
                  {[d.documentTypeLabel ?? d.documentType, d.documentNumber, d.validTo ? t('tax.capture.until', { date: f.date(d.validTo) }) : null].filter(Boolean).join(' · ')}
                </option>
              ))}
            </select>
          </div>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

export function ExemptionsTab() {
  const t = useT()
  const f = useFormat()
  const list = useTaxExemptions()
  const { setExemptionActive } = useTaxMutations()
  const [modal, setModal] = useState<TaxExemptionDto | 'new' | null>(null)
  const [linking, setLinking] = useState<TaxExemptionDto | null>(null)
  const options = useComponentOptions()
  const scopeText = (e: TaxExemptionDto) =>
    scopeOf(e.scope) === 'COMPONENTS' ? t('tax.exemptions.scopeOnly', { names: componentNames(e.componentCodes, options) }) : t('tax.exemptions.scopes.ALL')

  const toggle = async (e: TaxExemptionDto) => {
    try {
      await setExemptionActive.mutateAsync({ id: e.id!, active: !e.isActive })
      toast.success(t(e.isActive ? 'tax.exemptions.deactivated' : 'tax.exemptions.reactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const columns = useMemo<DataColumn<TaxExemptionDto>[]>(
    () => [
      { id: 'client', header: t('tax.exemptions.client'), cell: (e) => <b>{e.clientName}</b>, exportValue: (e) => e.clientName ?? '', sortValue: (e) => e.clientName ?? '', card: 'title' },
      { id: 'type', header: t('tax.exemptions.type'), cell: (e) => e.documentTypeLabel ?? e.exemptionType, sortValue: (e) => e.documentTypeLabel ?? e.exemptionType ?? '' },
      { id: 'cert', header: t('tax.exemptions.certificate'), cell: (e) => e.certificateNumber, sortValue: (e) => e.certificateNumber ?? '' },
      {
        id: 'document',
        header: t('tax.exemptions.document'),
        cell: (e) => (e.missingDocument ? <Chip tone="warn" title={t('tax.exemptions.missingDocumentHelp')}>{t('tax.exemptions.missingDocument')}</Chip> : (e.documentTypeLabel ?? e.documentType ?? '—')),
        exportValue: (e) => (e.missingDocument ? t('tax.exemptions.missingDocument') : (e.documentTypeLabel ?? e.documentType ?? '')),
        sortValue: (e) => (e.missingDocument ? '' : (e.documentTypeLabel ?? e.documentType ?? '')),
      },
      { id: 'scope', header: t('tax.exemptions.scope'), cell: scopeText, exportValue: scopeText, sortValue: scopeText },
      { id: 'from', header: t('tax.exemptions.from'), cell: (e) => f.date(e.validFrom), exportValue: (e) => e.validFrom ?? '', sortValue: (e) => e.validFrom ?? '' },
      { id: 'to', header: t('tax.exemptions.to'), cell: (e) => (e.validTo ? f.date(e.validTo) : t('tax.rates.open')), exportValue: (e) => e.validTo ?? '', sortValue: (e) => e.validTo ?? '9999-12-31' },
      { id: 'status', header: t('tax.exemptions.status'), cell: (e) => <Chip tone={STATUS_TONE[e.status ?? 'INACTIVE']}>{t(`tax.exemptions.statuses.${e.status}`)}</Chip>, exportValue: (e) => t(`tax.exemptions.statuses.${e.status}`), sortValue: (e) => e.status ?? '' },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, options],
  )

  return (
    <Panel
      flush
      icon={<IconDoc />}
      title={t('tax.exemptions.panel')}
      badge={list.isPending ? undefined : (list.data ?? NO_ROWS).length}
      actions={<button type="button" className="btn flow" onClick={() => setModal('new')}>{t('tax.exemptions.new')}</button>}
    >
      <div className="pb">
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('tax.exemptions.panel')}
            columns={columns}
            rows={list.data}
            rowKey={(e) => String(e.id)}
            defaultSort={{ id: 'from', desc: true }}
            empty={t('tax.exemptions.empty')}
            rowActions={[
              { key: 'link', label: t('tax.exemptions.link'), icon: <IconDoc />, onClick: (e) => setLinking(e), visible: (e) => !!e.missingDocument },
              { key: 'edit', label: t('tax.exemptions.edit'), icon: <IconEdit />, onClick: (e) => setModal(e), visible: (e) => !e.missingDocument },
              { key: 'deactivate', label: t('tax.exemptions.deactivate'), icon: <IconPower />, onClick: (e) => void toggle(e), visible: (e) => !!e.isActive },
              { key: 'reactivate', label: t('tax.exemptions.reactivate'), icon: <IconCheckCircle />, onClick: (e) => void toggle(e), visible: (e) => !e.isActive },
            ]}
          />
        )}
        <p className="help">{t('tax.exemptions.help')}</p>
      </div>
      {modal === 'new' && <ExemptionCaptureModal onClose={() => setModal(null)} />}
      {modal && modal !== 'new' && <ExemptionModal key={modal.id} exemption={modal} onClose={() => setModal(null)} />}
      {linking && <LinkDocumentModal key={linking.id} exemption={linking} onClose={() => setLinking(null)} />}
    </Panel>
  )
}
