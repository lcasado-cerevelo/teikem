// Sección «Correo» del formulario de integraciones (Lote 133, P11): el buzón (servidor IMAP, usuario, contraseña, carpeta), qué correos se toman (asunto y remitente ORIGINAL permitido), qué se lee
// (adjuntos por nombre y tipo y/o el cuerpo con campos a leer por etiqueta, expresión regular o bloque de renglones) y a qué columna de la plantilla va cada campo.
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Select, TextInput, Toggle } from '../../kernel/ui'
import { BodyFieldsEditor } from './BodyFieldsEditor'
import { ATTACHMENT_MODES, EMAIL_PROVIDERS, MARK_MODES, MATCH_OPERATORS } from './emailRules'
import { ReadersSection } from './ReadersSection'
import type { IntegrationValues } from './integrationRules'

export function EmailSection({ form, hasSavedPassword, clientOptions, templateOptions }: { form: UseFormReturn<IntegrationValues>; hasSavedPassword: boolean; clientOptions: { value: string; label: string }[]; templateOptions: { value: string; label: string }[] }) {
  const t = useT()
  const email = useWatch({ control: form.control, name: 'email' })
  const m365 = email?.provider === 'MICROSOFT365'
  const opOptions = MATCH_OPERATORS.map((o) => ({ value: o, label: t(`integrations.email.operator.${o}`) }))
  const bodyError = (form.formState.errors.email as { bodyFields?: { message?: string; root?: { message?: string } } } | undefined)?.bodyFields
  return (
    <>
      <fieldset className="int-scopes">
        <legend>{t('integrations.email.mailbox')}</legend>
        <p className="help">{t('integrations.email.mailboxHelp')}</p>
        <Field name="email.provider" label={t('integrations.email.provider')} help={t(m365 ? 'integrations.email.providerHelpM365' : 'integrations.email.providerHelpImap')}>
          <Select options={EMAIL_PROVIDERS.map((p) => ({ value: p, label: t(`integrations.email.providerOption.${p}`) }))} />
        </Field>
        {m365 ? (
          <>
            <p className="help">{t('integrations.email.m365Help')}</p>
            <div className="r2">
              <Field name="email.tenantId" label={t('integrations.email.tenantId')} required help={t('integrations.email.tenantIdHelp')}>
                <TextInput placeholder="contoso.onmicrosoft.com" autoComplete="off" />
              </Field>
              <Field name="email.clientId" label={t('integrations.email.clientId')} required help={t('integrations.email.clientIdHelp')}>
                <TextInput autoComplete="off" />
              </Field>
            </div>
          </>
        ) : (
          <>
            <div className="r2">
              <Field name="email.host" label={t('integrations.email.host')} required help={t('integrations.email.hostHelp')}>
                <TextInput placeholder="imap.gmail.com" autoComplete="off" />
              </Field>
              <Field name="email.port" label={t('integrations.email.port')} required>
                <TextInput inputMode="numeric" maxLength={5} />
              </Field>
            </div>
            <Field name="email.useSsl" label={t('integrations.email.useSsl')}>
              <Toggle text={t('integrations.email.useSsl')} />
            </Field>
          </>
        )}
        <div className="r2">
          <Field name="email.username" label={t(m365 ? 'integrations.email.mailboxAddress' : 'integrations.email.username')} required help={m365 ? t('integrations.email.mailboxAddressHelp') : undefined}>
            <TextInput autoComplete="off" />
          </Field>
          <Field name="email.password" label={t(m365 ? 'integrations.email.clientSecret' : 'integrations.email.password')} required={!hasSavedPassword} help={hasSavedPassword ? t('integrations.email.passwordKept') : t(m365 ? 'integrations.email.clientSecretHelp' : 'integrations.email.passwordHelp')}>
            <TextInput type="password" autoComplete="new-password" />
          </Field>
        </div>
        <div className="r2">
          <Field name="email.mailbox" label={t('integrations.email.folder')} required>
            <TextInput />
          </Field>
          {email?.markMode === 'MAILBOX' && (
            <Field name="email.processedFolder" label={t('integrations.email.processed')} help={t('integrations.email.processedHelp')}>
              <TextInput />
            </Field>
          )}
        </div>
        <div className="r2">
          <Field name="email.markMode" label={t('integrations.email.markMode')} help={t(email?.markMode === 'MAILBOX' ? 'integrations.email.markModeHelpMailbox' : 'integrations.email.markModeHelpLog')}>
            <Select options={MARK_MODES.map((m) => ({ value: m, label: t(`integrations.email.markModeOption.${m}`) }))} />
          </Field>
          {email?.markMode !== 'MAILBOX' && (
            <Field name="email.readSince" label={t('integrations.email.readSince')} help={t('integrations.email.readSinceHelp')}>
              <DateInput />
            </Field>
          )}
        </div>
      </fieldset>

      <fieldset className="int-scopes">
        <legend>{t('integrations.email.which')}</legend>
        <p className="help">{t('integrations.email.whichHelp')}</p>
        <div className="r2">
          <Field name="email.subjectOperator" label={t('integrations.email.subjectOp')}>
            <Select options={opOptions} />
          </Field>
          <Field name="email.subjectValue" label={t('integrations.email.subject')} help={t('integrations.email.subjectHelp')}>
            <TextInput placeholder="PR Orden" />
          </Field>
        </div>
        <Field name="email.allowedSenders" label={t('integrations.email.senders')} help={t('integrations.email.sendersHelp')}>
          <TextInput placeholder="truss.com, rep@truss.com" />
        </Field>
      </fieldset>

      <fieldset className="int-scopes">
        <legend>{t('integrations.email.what')}</legend>
        <Field name="email.attachmentMode" label={t('integrations.email.attachments')}>
          <Select options={ATTACHMENT_MODES.map((m) => ({ value: m, label: t(`integrations.email.mode.${m}`) }))} />
        </Field>
        {email?.attachmentMode === 'BY_NAME' && (
          <div className="r2">
            <Field name="email.attachmentNameOperator" label={t('integrations.email.nameOp')}>
              <Select options={opOptions} />
            </Field>
            <Field name="email.attachmentNameValue" label={t('integrations.email.nameValue')} required>
              <TextInput placeholder="ordenes-*.csv" />
            </Field>
          </div>
        )}
        <div className="r2">
          <Field name="email.attachmentTypes" label={t('integrations.email.types')} help={t('integrations.email.typesHelp')}>
            <TextInput />
          </Field>
          <Field name="email.requireAttachment" label={t('integrations.email.requireAttachment')}>
            <Toggle text={t('integrations.email.requireAttachment')} />
          </Field>
        </div>
        <Field name="email.readBody" label={t('integrations.email.readBody')}>
          <Toggle text={t('integrations.email.readBody')} />
        </Field>
        <Field name="email.readPdf" label={t('integrations.email.readPdf')} help={t('integrations.email.readPdfHelp')}>
          <Toggle text={t('integrations.email.readPdf')} />
        </Field>
        {(email?.readBody || email?.readPdf) && (
          <>
            <p className="help">{t('integrations.email.bodyHelp')}</p>
            <BodyFieldsEditor form={form} base="email.bodyFields" error={bodyError?.message ?? bodyError?.root?.message} />
          </>
        )}
      </fieldset>

      <ReadersSection form={form} clientOptions={clientOptions} templateOptions={templateOptions} />
    </>
  )
}
