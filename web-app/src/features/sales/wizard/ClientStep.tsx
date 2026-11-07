// Lote 223 (P3) — paso 1 del pedido guiado: el CLIENTE (buscador por número o nombre, o «Cliente nuevo» con el alta mínima), la casilla Urgente con su nota y la
// forma de pago del pedido, prellenada con la del cliente y editable (si cambia, queda la marca «cambió»). El almacén solo se pide si la compañía tiene más de uno.
import { useEffect, useState } from 'react'
import { Can } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useLookups } from '../../../kernel/catalogs'
import { useT } from '../../../kernel/i18n'
import { Chip, Spinner } from '../../../kernel/ui'
import type { components } from '../../../kernel/api/schema'
import { WarehousePicker } from '../../warehouse/pickers'
import { isCreditTerm } from '../salesRules'
import { NewClientModal } from './NewClientModal'
import { useClientSearch, type ClientListItemDto } from './wizardApi'
import { NOTES_MAX, withPaymentMethod, type WizardState } from './wizardRules'

type ClientDetailDto = components['schemas']['ClientDetailDto']

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

export interface ClientStepProps {
  state: WizardState
  onChange: (next: (s: WizardState) => WizardState) => void
  onPickClient: (publicId: string | null, label: string) => void
  /** Ficha del cliente escogido (pueblo, estatus, forma de pago, exige firma). */
  client: ClientDetailDto | undefined
  clientLoading: boolean
  paymentChanged: boolean
  needsWarehouse: boolean
  /** Alta mínima hecha: el cliente queda escogido. */
  onClientCreated: (client: ClientDetailDto) => void
}

export function ClientStep({ state, onChange, onPickClient, client, clientLoading, paymentChanged, needsWarehouse, onClientCreated }: ClientStepProps) {
  const t = useT()
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const search = useDebounced(q.trim())
  const results = useClientSearch(search)
  const { data: methods = [] } = useLookups('SalesPaymentMethod')
  const { data: termOptions = [] } = useLookups('PaymentTerm', { includeDisabled: true })
  const isProspect = (client?.status ?? '').toUpperCase() === 'PROSPECT'
  const rows: ClientListItemDto[] = (results.data ?? []).slice(0, 30)

  return (
    <section className="wz-step" aria-labelledby="wz-step-title">
      <h2 id="wz-step-title">{t('salesWizard.client.title')}</h2>
      {state.clientPublicId ? (
        <div className="wz-picked" role="status">
          <div className="wz-picked-main">
            <b>{client?.name ?? state.clientLabel}</b>
            <span className="help">
              {[client?.code ? t('salesWizard.client.number', { code: client.code }) : '', client?.physicalAddress?.city ?? ''].filter(Boolean).join(' · ')}
            </span>
            {isProspect && <Chip tone="warn">{t('salesWizard.client.prospect')}</Chip>}
            {clientLoading && <Spinner label={t('common.loading')} />}
          </div>
          <button type="button" className="btn" onClick={() => onPickClient(null, '')}>{t('salesWizard.client.change')}</button>
        </div>
      ) : (
        <div className="wz-search">
          <div className="f">
            <label htmlFor="wz-client-q">{t('salesWizard.client.search')}</label>
            <input id="wz-client-q" type="search" value={q} autoComplete="off" placeholder={t('salesWizard.client.searchPlaceholder')} onChange={(e) => setQ(e.target.value)} />
            <p className="help">{t('salesWizard.client.searchHelp')}</p>
          </div>
          {results.isError && <p className="ferr" role="alert">{applyProblemDetails(results.error).title}</p>}
          {search !== '' && results.isPending && <Spinner block />}
          {search !== '' && results.data && rows.length === 0 && <p className="help" role="status">{t('salesWizard.client.none')}</p>}
          {rows.length > 0 && (
            <ul className="wz-results" aria-label={t('salesWizard.client.results')}>
              {rows.map((c) => (
                <li key={c.publicId}>
                  <button type="button" className="unrow" onClick={() => onPickClient(c.publicId ?? null, `${c.code} · ${c.name}`)}>
                    <b>{c.name}</b>
                    <span className="meta">
                      {t('salesWizard.client.number', { code: c.code ?? '' })}
                      {(c.status ?? '').toUpperCase() === 'PROSPECT' && <> · {t('salesWizard.client.prospect')}</>}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Can perm="clients.create_basic|clients.create">
            <button type="button" className="btn" onClick={() => setCreating(true)}>{t('salesWizard.client.new')}</button>
          </Can>
        </div>
      )}

      {needsWarehouse && (
        <div className="f">
          <label htmlFor="wz-wh">{t('salesWizard.client.warehouse')}</label>
          <WarehousePicker id="wz-wh" value={state.warehousePublicId} onChange={(id) => onChange((s) => ({ ...s, warehousePublicId: id }))} />
          <p className="help">{t('salesWizard.client.warehouseHelp')}</p>
        </div>
      )}

      <fieldset className="wz-fieldset">
        <legend>{t('salesWizard.client.orderData')}</legend>
        <label className="sw">
          <input type="checkbox" role="switch" checked={state.isUrgent} onChange={(e) => onChange((s) => ({ ...s, isUrgent: e.target.checked }))} />
          <span className="tk" aria-hidden="true" />
          <span>{t('salesWizard.client.urgent')}</span>
        </label>
        <div className="f">
          <label htmlFor="wz-notes">{t('salesWizard.client.notes')}</label>
          <textarea id="wz-notes" rows={3} maxLength={NOTES_MAX} value={state.notes} aria-describedby="wz-notes-count" onChange={(e) => onChange((s) => ({ ...s, notes: e.target.value }))} />
          <p className="help" id="wz-notes-count">{t('salesWizard.client.notesCount', { n: state.notes.length, max: NOTES_MAX })}</p>
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="wz-pay">{t('salesWizard.client.payment')}</label>
            <select id="wz-pay" value={state.paymentMethod} onChange={(e) => onChange((s) => withPaymentMethod(s, e.target.value))}>
              <option value="">{t('salesWizard.client.paymentNone')}</option>
              {methods.map((m) => (
                <option key={m.code} value={m.code}>{m.label}</option>
              ))}
            </select>
            {paymentChanged && (
              <p className="help">
                <Chip tone="warn">{t('sales.badge.paymentChanged')}</Chip> {t('salesWizard.client.paymentChangedHelp')}
              </p>
            )}
          </div>
          {state.paymentMethod === 'CREDIT' && (
            <div className="f">
              <label htmlFor="wz-term">{t('salesWizard.client.term')}</label>
              <select id="wz-term" value={state.paymentTerm} onChange={(e) => onChange((s) => ({ ...s, paymentTerm: e.target.value }))}>
                <option value="">{t('salesWizard.client.termNone')}</option>
                {termOptions.filter((o) => isCreditTerm(o.code)).map((o) => (
                  <option key={o.code} value={o.code}>{o.label}</option>
                ))}
              </select>
            </div>
          )}
        </div>
      </fieldset>
      <NewClientModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(c) => {
          setCreating(false)
          onClientCreated(c)
        }}
      />
    </section>
  )
}
