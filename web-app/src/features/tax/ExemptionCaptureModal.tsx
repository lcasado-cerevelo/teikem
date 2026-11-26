// Lote 248 — captura única de la exención de IVU: el certificado y la exención en un solo paso (tax.manage, módulo TAX). Dos modos:
// - archivo nuevo: tipo de documento «de impuestos», número, desde, hasta (obligatorio) y el archivo; crea el documento del cliente y la exención juntos;
// - documento ya subido (`document`): solo el alcance; el número y las fechas son los del documento (se corrigen en Documentos del cliente).
// Alcance: todo el impuesto o solo algunos componentes de la tasa vigente (p. ej. estatal, municipal o ambos). Sin cliente fijo muestra el buscador de cliente.
import { useId, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ClientPicker, DateField, Modal, toast } from '../../kernel/ui'
import { useCaptureExemption, useClientDocumentTypes } from '../clients/documentsApi'
import type { ClientDocumentDto } from '../clients/clientSalesApi'
import { useComponentOptions } from './useComponentOptions'
import { captureError, scopeBody, scopeOf, type ExemptionScope } from './taxRules'

const SCOPES: ExemptionScope[] = ['ALL', 'COMPONENTS']
const NO_CODES: string[] = []

export interface ExemptionCaptureModalProps {
  /** Cliente fijo (ficha del cliente). Sin él, se escoge en el formulario. */
  clientPublicId?: string | null
  clientName?: string | null
  /** Documento ya subido que respaldará la exención. */
  document?: ClientDocumentDto | null
  /** Renovar: parte del alcance de la exención anterior. */
  renewScope?: string | null
  renewComponents?: readonly string[] | null
  onClose: () => void
}

export function ExemptionCaptureModal({ clientPublicId, clientName, document, renewScope, renewComponents, onClose }: ExemptionCaptureModalProps) {
  const t = useT()
  const f = useFormat()
  const scopeName = useId()
  const fileRef = useRef<HTMLInputElement>(null)
  const types = useClientDocumentTypes()
  const [picked, setPicked] = useState<string | null>(null)
  const clientId = clientPublicId ?? picked
  const capture = useCaptureExemption(clientId ?? '')
  const taxTypes = (types.data ?? []).filter((x) => x.marks.isTax)
  const [type, setType] = useState('')
  const [number, setNumber] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [hasFile, setHasFile] = useState(false)
  const [scope, setScope] = useState<ExemptionScope>(scopeOf(renewScope))
  const [components, setComponents] = useState<string[]>([...(renewComponents ?? NO_CODES)])
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [details, setDetails] = useState<string[]>([])
  const options = useComponentOptions(renewComponents ?? NO_CODES)
  const existing = document != null
  const busy = capture.isPending

  const toggle = (code: string, on: boolean) => setComponents((c) => (on ? [...c.filter((x) => x !== code), code] : c.filter((x) => x !== code)))

  const save = async () => {
    setError(null)
    setDetails([])
    const problem = captureError({ clientPublicId: clientId, existingDocument: existing, type, hasFile, number, from, to, scope, components })
    if (problem) return setError(t(`tax.capture.errors.${problem}`))
    try {
      await capture.mutateAsync({
        ...(existing
          ? { clientDocumentPublicId: document.publicId }
          : { file: fileRef.current?.files?.[0], documentType: type, documentNumber: number, validFrom: from, validTo: to }),
        ...scopeBody({ scope, components }),
        notes,
      })
      toast.success(t('tax.capture.done'))
      onClose()
    } catch (e) {
      const p = applyProblemDetails(e)
      setError(p.title)
      setDetails(Object.values(p.errors).flat().filter((m) => m !== p.title))
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t(existing ? 'tax.capture.titleDocument' : renewScope ? 'tax.capture.titleRenew' : 'tax.capture.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>{busy ? t('common.loading') : t('tax.capture.save')}</button>
        </>
      }
    >
      <div className="pb tax-form txc-form">
        <div className="f">
          <label htmlFor="txc-client">{t('tax.exemptions.client')}</label>
          {clientPublicId ? <input id="txc-client" value={clientName ?? ''} readOnly /> : <ClientPicker id="txc-client" value={picked} onChange={(id) => setPicked(id)} />}
        </div>
        {existing ? (
          <div className="txc-doc" role="group" aria-label={t('tax.capture.document')}>
            <b>{document.documentTypeLabel ?? document.documentType}</b>
            <span>{[document.documentNumber ?? t('tax.capture.noNumber'), document.validTo ? t('tax.capture.until', { date: f.date(document.validTo) }) : null].filter(Boolean).join(' · ')}</span>
            <span className="help">{t('tax.capture.documentHelp')}</span>
          </div>
        ) : (
          <>
            <div className="r2">
              <div className="f">
                <label htmlFor="txc-type">{t('clients.documents.type')}</label>
                <select id="txc-type" value={type} onChange={(e) => setType(e.target.value)}>
                  <option value="">{t('clients.documents.typePlaceholder')}</option>
                  {taxTypes.map((o) => (
                    <option key={o.code} value={o.code}>{o.label}</option>
                  ))}
                </select>
              </div>
              <div className="f">
                <label htmlFor="txc-number">{t('tax.exemptions.certificate')}</label>
                <input id="txc-number" value={number} maxLength={60} autoComplete="off" onChange={(e) => setNumber(e.target.value)} />
              </div>
            </div>
            <div className="r2">
              <div className="f">
                <label htmlFor="txc-from">{t('tax.exemptions.from')}</label>
                <DateField id="txc-from" value={from} onChange={setFrom} />
              </div>
              <div className="f">
                <label htmlFor="txc-to">{t('tax.capture.to')}</label>
                <DateField id="txc-to" value={to} onChange={setTo} />
              </div>
            </div>
            <div className="f">
              <label htmlFor="txc-file">{t('clients.documents.file')}</label>
              <input id="txc-file" type="file" ref={fileRef} onChange={(e) => setHasFile((e.target.files?.length ?? 0) > 0)} />
            </div>
          </>
        )}
        <fieldset className="tax-comps tax-scope">
          <legend>{t('tax.exemptions.scope')}</legend>
          <div className="tax-scope-options" role="radiogroup" aria-label={t('tax.exemptions.scope')}>
            {SCOPES.map((s) => (
              <label key={s} className="tax-check">
                <input type="radio" name={scopeName} value={s} checked={scope === s} onChange={() => setScope(s)} />
                <span>{t(`tax.exemptions.scopes.${s}`)}</span>
              </label>
            ))}
          </div>
          {scope === 'COMPONENTS' ? (
            options.length === 0 ? (
              <p className="help" role="status">{t('tax.exemptions.noComponents')}</p>
            ) : (
              <div className="tax-scope-options" role="group" aria-label={t('tax.exemptions.components')}>
                {options.map((o) => (
                  <label key={o.code} className="tax-check">
                    <input type="checkbox" checked={components.includes(o.code)} onChange={(e) => toggle(o.code, e.target.checked)} />
                    <span>{o.percent === null ? t('tax.exemptions.componentNotInRate', { name: o.name }) : t('tax.exemptions.componentOption', { name: o.name, pct: f.number(o.percent, { maximumFractionDigits: 4 }) })}</span>
                  </label>
                ))}
              </div>
            )
          ) : null}
          <p className="help">{t(scope === 'COMPONENTS' ? 'tax.capture.scopeHelpComponents' : 'tax.capture.scopeHelpAll')}</p>
        </fieldset>
        <div className="f">
          <label htmlFor="txc-notes">{t('tax.exemptions.notes')}</label>
          <textarea id="txc-notes" rows={2} maxLength={300} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        {error && (
          <div role="alert" className="ferr">
            <p>{error}</p>
            {details.map((m) => (
              <p key={m}>{m}</p>
            ))}
          </div>
        )}
      </div>
    </Modal>
  )
}
