// Sección «Servidor remoto» del formulario de integraciones (Lote 134, P11): SFTP y FTP. Servidor, puerto, usuario, contraseña o llave privada (cifradas, no se vuelven a mostrar), carpeta remota,
// carpetas de procesados y de errores (los archivos se MUEVEN, nunca se borran), huella SHA-256 del servidor SFTP (fijada) o FTPS, y el filtro de archivos por nombre y tipo.
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Select, TextArea, TextInput, Toggle } from '../../kernel/ui'
import { MATCH_OPERATORS } from './emailRules'
import type { IntegrationValues } from './integrationRules'
import { AUTH_KINDS } from './remoteRules'

export function RemoteSection({ form, kind, hasSavedSecret }: { form: UseFormReturn<IntegrationValues>; kind: 'SFTP' | 'FTP'; hasSavedSecret: boolean }) {
  const t = useT()
  const remote = useWatch({ control: form.control, name: 'remote' })
  const key = kind === 'SFTP' && remote?.authKind === 'KEY'
  return (
    <fieldset className="int-scopes">
      <legend>{t('integrations.remote.section')}</legend>
      <p className="help">{t('integrations.remote.sectionHelp')}</p>
      <div className="r2">
        <Field name="remote.host" label={t('integrations.remote.host')} required>
          <TextInput placeholder={kind === 'SFTP' ? 'sftp.cliente.com' : 'ftp.cliente.com'} autoComplete="off" />
        </Field>
        <Field name="remote.port" label={t('integrations.remote.port')} required>
          <TextInput inputMode="numeric" maxLength={5} />
        </Field>
      </div>
      <div className="r2">
        <Field name="remote.username" label={t('integrations.remote.username')} required>
          <TextInput autoComplete="off" />
        </Field>
        {kind === 'SFTP' && (
          <Field name="remote.authKind" label={t('integrations.remote.auth')}>
            <Select options={AUTH_KINDS.map((a) => ({ value: a, label: t(`integrations.remote.authOption.${a}`) }))} />
          </Field>
        )}
      </div>
      {key && (
        <Field name="remote.privateKey" label={t('integrations.remote.privateKey')} required={!hasSavedSecret} help={hasSavedSecret ? t('integrations.remote.secretKept') : t('integrations.remote.secretHelp')}>
          <TextArea rows={4} autoComplete="off" />
        </Field>
      )}
      <Field name="remote.password" label={key ? t('integrations.remote.passphrase') : t('integrations.remote.password')} required={!key && !hasSavedSecret} help={hasSavedSecret ? t('integrations.remote.secretKept') : t('integrations.remote.secretHelp')}>
        <TextInput type="password" autoComplete="new-password" />
      </Field>
      <Field name="remote.dir" label={t('integrations.remote.dir')} required help={t('integrations.remote.dirHelp')}>
        <TextInput placeholder="/entrada" />
      </Field>
      <div className="r2">
        <Field name="remote.processedDir" label={t('integrations.remote.processed')} help={t('integrations.remote.processedHelp')}>
          <TextInput />
        </Field>
        <Field name="remote.errorDir" label={t('integrations.remote.errorsDir')}>
          <TextInput />
        </Field>
      </div>
      {kind === 'SFTP' ? (
        <Field name="remote.hostFingerprint" label={t('integrations.remote.fingerprint')} help={t('integrations.remote.fingerprintHelp')}>
          <TextInput placeholder="SHA256:…" autoComplete="off" />
        </Field>
      ) : (
        <Field name="remote.useFtps" label={t('integrations.remote.useFtps')} help={t('integrations.remote.useFtpsHelp')}>
          <Toggle text={t('integrations.remote.useFtps')} />
        </Field>
      )}
      <div className="r2">
        <Field name="remote.fileNameOperator" label={t('integrations.remote.fileName')}>
          <Select options={MATCH_OPERATORS.map((o) => ({ value: o, label: t(`integrations.email.operator.${o}`) }))} />
        </Field>
        <Field name="remote.fileNameValue" label={t('integrations.remote.fileNameValue')} help={t('integrations.remote.fileNameHelp')}>
          <TextInput placeholder="ordenes-*.csv" />
        </Field>
      </div>
      <Field name="remote.fileTypes" label={t('integrations.remote.types')} help={t('integrations.remote.typesHelp')}>
        <TextInput />
      </Field>
    </fieldset>
  )
}
