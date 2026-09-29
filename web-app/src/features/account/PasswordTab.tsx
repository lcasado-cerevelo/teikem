import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useT } from '../../kernel/i18n/useT'
import { Field, Form, Panel, TextInput, toast } from '../../kernel/ui'
import { useChangePassword } from './api'
import { IconLock } from '../../kernel/ui/screenIcons'

/** Longitud mínima de Identity (DependencyInjection: Password.RequiredLength = 12). El resto lo valida el servidor. */
export const MIN_PASSWORD_LENGTH = 12

/** Pestaña Contraseña: PUT /api/v1/auth/password. Los errores del servidor (brechas, contraseña actual) van bajo su campo. */
export function PasswordTab() {
  const t = useT()
  const change = useChangePassword()
  const schema = useMemo(
    () =>
      z
        .object({
          currentPassword: z.string().min(1, t('account.password.errors.currentRequired')),
          newPassword: z.string().min(MIN_PASSWORD_LENGTH, t('account.password.errors.minLength', { min: MIN_PASSWORD_LENGTH })),
          confirmPassword: z.string(),
        })
        .refine((v) => v.confirmPassword === v.newPassword, {
          path: ['confirmPassword'],
          message: t('account.password.errors.mismatch'),
        }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })
  const busy = form.formState.isSubmitting

  return (
    <Panel icon={<IconLock />} title={t('account.password.title')} subtitle={t('account.password.subtitle')} className="acct-narrow">
      <Form
        form={form}
        onSubmit={async (v) => {
          await change.mutateAsync({ currentPassword: v.currentPassword, newPassword: v.newPassword })
          form.reset()
          toast.success(t('account.password.changed'))
        }}
      >
        <Field name="currentPassword" label={t('account.password.current')} required>
          <TextInput type="password" autoComplete="current-password" maxLength={256} />
        </Field>
        <Field
          name="newPassword"
          label={t('account.password.new')}
          required
          help={t('account.password.help', { min: MIN_PASSWORD_LENGTH })}
        >
          <TextInput type="password" autoComplete="new-password" maxLength={256} />
        </Field>
        <Field name="confirmPassword" label={t('account.password.confirm')} required>
          <TextInput type="password" autoComplete="new-password" maxLength={256} />
        </Field>
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('account.password.submit')}
          </button>
        </div>
      </Form>
    </Panel>
  )
}
