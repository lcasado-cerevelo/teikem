// Lote 249 — «Quién autorizó el cambio»: nombre y cargo de quien lo autorizó en el cliente, su contacto (opcional), cómo y cuándo, el motivo y la evidencia
// (captura del correo o del mensaje; se sube antes de guardar y es obligatoria si el ajuste de la compañía o del cliente lo pide). Lo usan la modificación y la adición.
import { useRef, useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { DateTimeField } from '../../../kernel/ui'
import { useClient } from '../../clients/api'
import { AUTH_NAME_MAX, AUTH_TITLE_MAX, CHANNELS, REASON_MAX, type AuthDraft, type AuthErrorKey } from './changeRules'
import { useUploadChangeAttachment } from './changesApi'
import './changes.css'

export function ChangeAuthorizationStep({ orderPublicId, clientPublicId, auth, onChange, attachmentRequired, error }: {
  orderPublicId: string
  clientPublicId: string | null
  auth: AuthDraft
  onChange: (patch: Partial<AuthDraft>) => void
  attachmentRequired: boolean
  /** Lo que falta (se marca el campo); null = nada. */
  error: AuthErrorKey | null
}) {
  const t = useT()
  const client = useClient(clientPublicId, true)
  const upload = useUploadChangeAttachment(orderPublicId)
  const fileRef = useRef<HTMLInputElement>(null)
  const [fileName, setFileName] = useState<string | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const contacts = (client.data?.contacts ?? []).filter((c) => c.isActive !== false)

  const pick = async (file: File | undefined) => {
    setUploadError(null)
    if (!file) return
    try {
      const stored = await upload.mutateAsync(file)
      setFileName(stored.fileName ?? file.name)
      onChange({ attachmentId: stored.publicId ?? null })
    } catch (e) {
      setUploadError(applyProblemDetails(e).title)
    } finally {
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  return (
    <div className="soc-form" role="group" aria-label={t('salesChanges.auth.title')}>
      <p className="help">{t('salesChanges.auth.help')}</p>
      <div className="r2">
        <div className="f">
          <label htmlFor="soc-auth-name">{t('salesChanges.auth.name')}</label>
          <input id="soc-auth-name" value={auth.name} maxLength={AUTH_NAME_MAX} autoComplete="off" aria-required="true" aria-invalid={error === 'name'} onChange={(e) => onChange({ name: e.target.value })} />
          {error === 'name' && <p className="ferr" role="alert">{t('salesChanges.auth.errors.name')}</p>}
        </div>
        <div className="f">
          <label htmlFor="soc-auth-title">{t('salesChanges.auth.role')}</label>
          <input id="soc-auth-title" value={auth.title} maxLength={AUTH_TITLE_MAX} autoComplete="off" onChange={(e) => onChange({ title: e.target.value })} />
        </div>
      </div>
      {contacts.length > 0 && (
        <div className="f">
          <label htmlFor="soc-auth-contact">{t('salesChanges.auth.contact')}</label>
          <select id="soc-auth-contact" value={auth.contactId} onChange={(e) => onChange({ contactId: e.target.value })}>
            <option value="">{t('salesChanges.auth.contactNone')}</option>
            {contacts.map((c) => (
              <option key={c.id} value={String(c.id)}>{[c.fullName, c.role].filter(Boolean).join(' · ')}</option>
            ))}
          </select>
        </div>
      )}
      <div className="r2">
        <div className="f">
          <label htmlFor="soc-auth-channel">{t('salesChanges.auth.channel')}</label>
          <select id="soc-auth-channel" value={auth.channel} aria-required="true" aria-invalid={error === 'channel'} onChange={(e) => onChange({ channel: e.target.value })}>
            <option value="">{t('salesChanges.auth.channelPick')}</option>
            {CHANNELS.map((c) => (
              <option key={c} value={c}>{t(`salesChanges.channels.${c}`)}</option>
            ))}
          </select>
          {error === 'channel' && <p className="ferr" role="alert">{t('salesChanges.auth.errors.channel')}</p>}
        </div>
        <div className="f">
          <label htmlFor="soc-auth-at">{t('salesChanges.auth.when')}</label>
          <DateTimeField id="soc-auth-at" value={auth.at} onChange={(v) => onChange({ at: v })} aria-required={true} aria-invalid={error === 'at'} />
          {error === 'at' && <p className="ferr" role="alert">{t('salesChanges.auth.errors.at')}</p>}
        </div>
      </div>
      <div className="f">
        <label htmlFor="soc-auth-reason">{t('salesChanges.auth.reason')}</label>
        <textarea id="soc-auth-reason" rows={2} value={auth.reason} maxLength={REASON_MAX} aria-required="true" aria-invalid={error === 'reason'} onChange={(e) => onChange({ reason: e.target.value })} />
        {error === 'reason' && <p className="ferr" role="alert">{t('salesChanges.auth.errors.reason')}</p>}
      </div>
      <div className="f">
        <label htmlFor="soc-auth-file">{attachmentRequired ? t('salesChanges.auth.attachmentRequired') : t('salesChanges.auth.attachment')}</label>
        <div className="soc-attach">
          <input id="soc-auth-file" ref={fileRef} type="file" accept="image/*,application/pdf" disabled={upload.isPending} aria-required={attachmentRequired} onChange={(e) => void pick(e.target.files?.[0])} />
          {upload.isPending && <span className="help">{t('salesChanges.auth.uploading')}</span>}
          {auth.attachmentId && !upload.isPending && (
            <>
              <span className="help" role="status">{t('salesChanges.auth.attached', { name: fileName ?? '' })}</span>
              <button type="button" className="btn sm" onClick={() => { setFileName(null); onChange({ attachmentId: null }) }}>{t('salesChanges.auth.removeAttachment')}</button>
            </>
          )}
        </div>
        <p className="help">{attachmentRequired ? t('salesChanges.auth.attachmentRequiredHelp') : t('salesChanges.auth.attachmentHelp')}</p>
        {error === 'attachment' && <p className="ferr" role="alert">{t('salesChanges.auth.errors.attachment')}</p>}
        {uploadError && <p className="ferr" role="alert">{uploadError}</p>}
      </div>
    </div>
  )
}
