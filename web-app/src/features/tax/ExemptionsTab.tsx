// Impuestos → Exenciones (Lote 56): certificados de exención de clientes. Con una exención vigente nada se le cobra de impuesto y la línea
// queda con el certificado anotado; con la exención vencida se cobra y se avisa. Nunca se borran: se dan de baja y se pueden reactivar.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, type ChipTone, ClientPicker, type DataColumn, DataTable, DateField, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconCheckCircle, IconEdit, IconPower } from '../../kernel/ui/actionIcons'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { exemptionError, type ExemptionDraft } from './taxRules'
import { useTaxExemptions, useTaxMutations, type TaxExemptionDto } from './taxApi'

const STATUS_TONE: Record<string, ChipTone> = { VALID: 'deliv', SCHEDULED: 'warn', EXPIRED: 'fail', INACTIVE: 'neutral' }
const NO_ROWS: TaxExemptionDto[] = []

function ExemptionModal({ exemption, onClose }: { exemption: TaxExemptionDto | null; onClose: () => void }) {
  const t = useT()
  const { createExemption, updateExemption } = useTaxMutations()
  const editing = exemption !== null
  const [clientPublicId, setClientPublicId] = useState<string | null>(null)
  const [draft, setDraft] = useState<ExemptionDraft>({
    clientId: exemption?.clientId ?? null,
    type: exemption?.exemptionType ?? '',
    certificate: exemption?.certificateNumber ?? '',
    from: exemption?.validFrom ?? '',
    to: exemption?.validTo ?? '',
    notes: exemption?.notes ?? '',
  })
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<ExemptionDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const busy = createExemption.isPending || updateExemption.isPending

  const save = async () => {
    setError(null)
    const problem = exemptionError(draft)
    if (problem) return setError(t(`tax.exemptions.errors.${problem}`))
    try {
      if (editing) {
        await updateExemption.mutateAsync({
          id: exemption.id!,
          body: { exemptionType: draft.type, certificateNumber: draft.certificate, validFrom: draft.from, validTo: draft.to || undefined, clearValidTo: draft.to ? undefined : true, notes: draft.notes },
        })
      } else {
        await createExemption.mutateAsync({ clientId: draft.clientId, exemptionType: draft.type, certificateNumber: draft.certificate, validFrom: draft.from, validTo: draft.to || undefined, notes: draft.notes || undefined })
      }
      toast.success(t(editing ? 'tax.exemptions.updated' : 'tax.exemptions.created'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t(editing ? 'tax.exemptions.edit' : 'tax.exemptions.new')}
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
          {editing ? (
            <input id="tx-client" value={exemption.clientName ?? ''} readOnly />
          ) : (
            <ClientPicker id="tx-client" value={clientPublicId} onChange={(publicId, client) => (setClientPublicId(publicId), set({ clientId: client?.id ?? null }))} />
          )}
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="tx-etype">{t('tax.exemptions.type')}</label>
            <input id="tx-etype" value={draft.type} maxLength={60} autoComplete="off" onChange={(e) => set({ type: e.target.value })} />
          </div>
          <div className="f">
            <label htmlFor="tx-cert">{t('tax.exemptions.certificate')}</label>
            <input id="tx-cert" value={draft.certificate} maxLength={60} autoComplete="off" onChange={(e) => set({ certificate: e.target.value })} />
          </div>
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="tx-efrom">{t('tax.exemptions.from')}</label>
            <DateField id="tx-efrom" value={draft.from} onChange={(v) => set({ from: v })} />
          </div>
          <div className="f">
            <label htmlFor="tx-eto">{t('tax.exemptions.to')}</label>
            <DateField id="tx-eto" value={draft.to} onChange={(v) => set({ to: v })} />
            <p className="help">{t('tax.exemptions.toHelp')}</p>
          </div>
        </div>
        <div className="f">
          <label htmlFor="tx-notes">{t('tax.exemptions.notes')}</label>
          <textarea id="tx-notes" rows={3} maxLength={300} value={draft.notes} onChange={(e) => set({ notes: e.target.value })} />
        </div>
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
      { id: 'type', header: t('tax.exemptions.type'), cell: (e) => e.exemptionType, sortValue: (e) => e.exemptionType ?? '' },
      { id: 'cert', header: t('tax.exemptions.certificate'), cell: (e) => e.certificateNumber, sortValue: (e) => e.certificateNumber ?? '' },
      { id: 'from', header: t('tax.exemptions.from'), cell: (e) => f.date(e.validFrom), exportValue: (e) => e.validFrom ?? '', sortValue: (e) => e.validFrom ?? '' },
      { id: 'to', header: t('tax.exemptions.to'), cell: (e) => (e.validTo ? f.date(e.validTo) : t('tax.rates.open')), exportValue: (e) => e.validTo ?? '', sortValue: (e) => e.validTo ?? '9999-12-31' },
      { id: 'status', header: t('tax.exemptions.status'), cell: (e) => <Chip tone={STATUS_TONE[e.status ?? 'INACTIVE']}>{t(`tax.exemptions.statuses.${e.status}`)}</Chip>, exportValue: (e) => t(`tax.exemptions.statuses.${e.status}`), sortValue: (e) => e.status ?? '' },
    ],
    [t, f],
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
              { key: 'edit', label: t('tax.exemptions.edit'), icon: <IconEdit />, onClick: (e) => setModal(e) },
              { key: 'deactivate', label: t('tax.exemptions.deactivate'), icon: <IconPower />, onClick: (e) => void toggle(e), visible: (e) => !!e.isActive },
              { key: 'reactivate', label: t('tax.exemptions.reactivate'), icon: <IconCheckCircle />, onClick: (e) => void toggle(e), visible: (e) => !e.isActive },
            ]}
          />
        )}
        <p className="help">{t('tax.exemptions.help')}</p>
      </div>
      {modal && <ExemptionModal key={modal === 'new' ? 'new' : modal.id} exemption={modal === 'new' ? null : modal} onClose={() => setModal(null)} />}
    </Panel>
  )
}
