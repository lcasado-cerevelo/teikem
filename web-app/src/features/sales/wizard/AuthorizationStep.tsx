// Lote 223 (P3) — paso 4 del pedido guiado: AUTORIZACIÓN. Nombre de quien autoriza el pedido en el negocio del cliente y su firma dibujada (obligatorias solo si
// el cliente las exige; si no, opcionales), el resumen final y, aparte, la ventana que pide la contraseña al «Enviar pedido» (`SubmitPasswordModal`).
import { useId, useState } from 'react'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Modal, SignaturePad, type SignatureStroke } from '../../../kernel/ui'
import { PasswordBox } from '../../../kernel/ui/PasswordBox'
import type { SalesOrderDto } from '../salesApi'
import { AUTHORIZED_BY_MAX, orderSummary, type WizardState } from './wizardRules'

export interface AuthorizationStepProps {
  state: WizardState
  onChange: (next: (s: WizardState) => WizardState) => void
  strokes: SignatureStroke[]
  onStrokes: (strokes: SignatureStroke[]) => void
  requiresSignature: boolean
  /** La orden ya tiene una firma subida (y no se ha vuelto a dibujar). */
  hasSavedSignature: boolean
  saved: SalesOrderDto | null
}

export function AuthorizationStep({ state, onChange, strokes, onStrokes, requiresSignature, hasSavedSignature, saved }: AuthorizationStepProps) {
  const t = useT()
  const f = useFormat()
  const summary = orderSummary(saved)
  const helpId = useId()
  return (
    <section className="wz-step" aria-labelledby="wz-step-title">
      <h2 id="wz-step-title">{t('salesWizard.auth.title')}</h2>
      {requiresSignature && <p className="note" role="status">{t('salesWizard.auth.required')}</p>}
      <div className="f">
        <label htmlFor="wz-auth-name">
          {t('salesWizard.auth.name')}
          {requiresSignature && <span className="req" aria-hidden="true"> *</span>}
        </label>
        <input
          id="wz-auth-name"
          value={state.authorizedByName}
          maxLength={AUTHORIZED_BY_MAX}
          autoComplete="off"
          aria-required={requiresSignature || undefined}
          onChange={(e) => onChange((s) => ({ ...s, authorizedByName: e.target.value }))}
        />
      </div>
      <SignaturePad label={t('salesWizard.auth.signature')} value={strokes} onChange={onStrokes} required={requiresSignature} describedBy={helpId} />
      <p className="help" id={helpId}>
        {hasSavedSignature && strokes.length === 0 ? t('salesWizard.auth.savedSignature') : requiresSignature ? t('salesWizard.auth.signatureRequiredHelp') : t('salesWizard.auth.signatureOptional')}
      </p>
      <dl className="wz-final-sum">
        <div><dt>{t('salesWizard.auth.client')}</dt><dd>{saved?.clientName ?? state.clientLabel}</dd></div>
        <div><dt>{t('salesWizard.auth.lines')}</dt><dd>{state.cart.length}</dd></div>
        <div><dt>{t('salesWizard.summary.total')}</dt><dd className="mono">{f.money(summary.total)}</dd></div>
      </dl>
      <p className="help">{t('salesWizard.auth.sendHelp')}</p>
    </section>
  )
}

/** La contraseña (y el código MFA si la cuenta lo tiene) para confirmar el envío del pedido. */
export function SubmitPasswordModal({ open, mfa, busy, error, field, onCancel, onConfirm }: {
  open: boolean
  mfa: boolean
  busy: boolean
  error: string | null
  /** Campo del error del servidor ('password' | 'mfaCode'), para marcarlo. */
  field: 'password' | 'mfaCode' | null
  onCancel: () => void
  onConfirm: (password: string, mfaCode: string) => void
}) {
  const t = useT()
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [missing, setMissing] = useState(false)
  const submit = () => {
    if (password === '') return setMissing(true)
    setMissing(false)
    onConfirm(password, code.trim())
  }
  return (
    <Modal
      open={open}
      size="sm"
      title={t('salesWizard.submit.title')}
      onClose={onCancel}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>{t('common.cancel')}</button>
          <button type="submit" form="wz-submit-form" className="btn flow" disabled={busy}>{busy ? t('salesWizard.submit.sending') : t('salesWizard.submit.confirm')}</button>
        </>
      }
    >
      <div className="pb">
        <form
          id="wz-submit-form"
          className="wz-form"
          onSubmit={(e) => {
            e.preventDefault()
            submit()
          }}
        >
          <p className="help">{t('salesWizard.submit.help')}</p>
          <div className="f">
            <label htmlFor="wz-pwd">{t('salesWizard.submit.password')}</label>
            <PasswordBox
              id="wz-pwd"
              autoComplete="current-password"
              value={password}
              aria-invalid={missing || field === 'password' || undefined}
              aria-describedby={missing || field === 'password' ? 'wz-pwd-err' : undefined}
              onChange={(e) => setPassword(e.target.value)}
            />
            {(missing || field === 'password') && <p className="ferr" id="wz-pwd-err">{missing ? t('salesWizard.submit.passwordRequired') : error}</p>}
          </div>
          {mfa && (
            <div className="f">
              <label htmlFor="wz-mfa">{t('salesWizard.submit.mfa')}</label>
              <input
                id="wz-mfa"
                inputMode="numeric"
                autoComplete="one-time-code"
                maxLength={10}
                value={code}
                aria-invalid={field === 'mfaCode' || undefined}
                aria-describedby={field === 'mfaCode' ? 'wz-mfa-err' : undefined}
                onChange={(e) => setCode(e.target.value)}
              />
              {field === 'mfaCode' && <p className="ferr" id="wz-mfa-err">{error}</p>}
            </div>
          )}
          {error && field === null && <p className="ferr" role="alert">{error}</p>}
        </form>
      </div>
    </Modal>
  )
}
