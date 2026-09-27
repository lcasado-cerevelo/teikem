import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/client'
import { useReauth } from '../../kernel/auth/reauthContext'
import { useT } from '../../kernel/i18n/useT'
import { Chip, Field, Form, Panel, TextInput, toast } from '../../kernel/ui'
import { useConfirmTotp, useDisableTotp, useEnrollTotp, type MfaEnrollResultDto } from './api'

type Step = { kind: 'idle' } | { kind: 'enrolling'; enroll: MfaEnrollResultDto } | { kind: 'recovery'; codes: string[] }

async function copyText(text: string, okMessage: string, failMessage: string) {
  try {
    if (!navigator.clipboard) throw new Error('clipboard')
    await navigator.clipboard.writeText(text)
    toast.success(okMessage)
  } catch {
    toast.error(failMessage)
  }
}

/** Paso 2 del alta: código de 6 dígitos de la app → POST /mfa/totp/confirm (error "Código inválido." bajo el campo). */
function ConfirmForm({ onConfirmed, onCancel }: { onConfirmed: (codes: string[]) => void; onCancel: () => void }) {
  const t = useT()
  const confirm = useConfirmTotp()
  const schema = useMemo(
    () => z.object({ code: z.string().trim().regex(/^\d{6}$/, t('account.mfa.errors.code')) }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { code: '' } })
  const busy = form.formState.isSubmitting
  return (
    <Form form={form} onSubmit={async (v) => onConfirmed(await confirm.mutateAsync(v.code))}>
      <Field name="code" label={t('auth.fields.mfaCode')} required>
        <TextInput inputMode="numeric" autoComplete="one-time-code" maxLength={6} />
      </Field>
      <div className="form-acts">
        <button type="button" className="btn" onClick={onCancel} disabled={busy}>
          {t('common.cancel')}
        </button>
        <button type="submit" className="btn flow" disabled={busy}>
          {busy ? t('common.loading') : t('account.mfa.confirm')}
        </button>
      </div>
    </Form>
  )
}

/**
 * Pestaña MFA (TOTP). Activar: enroll → clave manual y URI otpauth → confirmar con código → códigos de recuperación
 * (una sola vez). Desactivar: reautenticación (AAL2) y DELETE /mfa/totp.
 */
export function MfaTab() {
  const t = useT()
  const { me, reloadMe } = useSession()
  const { reauth } = useReauth()
  const enroll = useEnrollTotp()
  const disable = useDisableTotp()
  const [step, setStep] = useState<Step>({ kind: 'idle' })
  const enabled = me?.mfaEnabled ?? false

  const start = async () => {
    try {
      setStep({ kind: 'enrolling', enroll: await enroll.mutateAsync() })
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const turnOff = async () => {
    // Acción sensible: se pide la reautenticación antes (el API la exige con [RequireAal2]).
    if (!(await reauth())) return
    try {
      await disable.mutateAsync()
      await reloadMe()
      toast.success(t('account.mfa.disabled'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  if (step.kind === 'recovery') {
    const text = step.codes.join('\n')
    return (
      <Panel title={t('account.mfa.recoveryTitle')} subtitle={t('account.mfa.recoverySubtitle')} className="acct-narrow">
        <div className="codes" data-testid="recovery-codes">
          {step.codes.map((c) => (
            <span key={c}>{c}</span>
          ))}
        </div>
        <div className="form-acts">
          <button
            type="button"
            className="btn"
            onClick={() => void copyText(text, t('account.mfa.copied'), t('account.mfa.copyFailed'))}
          >
            {t('common.copy')}
          </button>
          <button type="button" className="btn flow" onClick={() => setStep({ kind: 'idle' })}>
            {t('account.mfa.recoveryDone')}
          </button>
        </div>
      </Panel>
    )
  }

  if (step.kind === 'enrolling') {
    const { secret, otpAuthUri } = step.enroll
    return (
      <Panel title={t('account.mfa.enrollTitle')} subtitle={t('account.mfa.enrollSubtitle')} className="acct-narrow">
        <p className="subtle" style={{ marginBottom: 4 }}>
          {t('account.mfa.secretLabel')}
        </p>
        <div className="secret" data-testid="mfa-secret">
          {secret}
        </div>
        <div className="acct-row">
          <button
            type="button"
            className="btn sm"
            onClick={() => void copyText(secret ?? '', t('account.mfa.copied'), t('account.mfa.copyFailed'))}
          >
            {t('account.mfa.copySecret')}
          </button>
          {otpAuthUri && (
            <a className="btn sm" href={otpAuthUri}>
              {t('account.mfa.openInApp')}
            </a>
          )}
        </div>
        {otpAuthUri && (
          <>
            <p className="subtle" style={{ marginBottom: 4 }}>
              {t('account.mfa.uriLabel')}
            </p>
            <p className="acct-uri" data-testid="mfa-uri">
              {otpAuthUri}
            </p>
          </>
        )}
        <ConfirmForm
          onCancel={() => setStep({ kind: 'idle' })}
          onConfirmed={(codes) => {
            setStep({ kind: 'recovery', codes })
            void reloadMe()
            toast.success(t('account.mfa.enabled'))
          }}
        />
      </Panel>
    )
  }

  return (
    <Panel title={t('account.mfa.title')} subtitle={t('account.mfa.subtitle')} className="acct-narrow">
      <div className="acct-row">
        <span>{t('account.mfa.state')}</span>
        <Chip tone={enabled ? 'deliv' : 'warn'}>{enabled ? t('account.mfa.on') : t('account.mfa.off')}</Chip>
      </div>
      <p className="note">{enabled ? t('account.mfa.enabledHelp') : t('account.mfa.disabledHelp')}</p>
      <div className="form-acts">
        {enabled ? (
          <button type="button" className="btn danger" disabled={disable.isPending} onClick={() => void turnOff()}>
            {t('account.mfa.disable')}
          </button>
        ) : (
          <button type="button" className="btn flow" disabled={enroll.isPending} onClick={() => void start()}>
            {t('account.mfa.enable')}
          </button>
        )}
      </div>
    </Panel>
  )
}
