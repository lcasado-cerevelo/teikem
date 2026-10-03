// Seguridad y auditoría → Sesiones y MFA (maqueta `auditoriaSesionesTab`): a la izquierda las sesiones activas de TODA la
// compañía (`GET /audit/sessions`: usuario, dispositivo, ubicación = IP, última actividad) con "Revocar" por fila y "Cerrar las
// demás sesiones" (`admin.users`; la propia se marca "Esta sesión" y no se revoca desde aquí: se cierra con Salir); a la derecha
// la política de la compañía (`Tenant.MfaRequired`, `Aal2WindowMinutes`, `SessionDays`, `DeviceSessionDays` por
// `PUT /tenant/settings` parcial). La política se edita con `admin.tenant`; sin él, solo lectura. Los 400 del servidor quedan
// bajo su campo (`Form` + `applyProblemDetails`).
import { useCallback, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/client'
import { useTenantSettings } from '../../../kernel/catalogs'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  EmptyState,
  Field,
  Form,
  IconGear,
  IconShield,
  IconTrash,
  NumberInput,
  Panel,
  Select,
  Spinner,
  Toggle,
  toast,
  type DataColumn,
  type RowAction,
} from '../../../kernel/ui'
import { describeDevice } from '../../account/format'
import { useCompanySessions, useRevokeCompanySession, useRevokeOtherSessions } from '../auditApi'
import { useSaveTenantSettings, type TenantSettingsDto } from '../tenantSettingsApi'
import {
  policyErrors,
  policyRequestBody,
  reauthOptions,
  sessionLocation,
  toPolicyValues,
  type CompanySessionDto,
  type PolicyValues,
} from './auditView'

const NO_SESSIONS: CompanySessionDto[] = []

/** Nombre del usuario de una sesión (sin nombre, el correo; sin ninguno, su id). */
const userText = (s: CompanySessionDto) => s.userName ?? s.userEmail ?? `#${s.userId}`

export function CompanySessionsTab() {
  return (
    <div className="aud-cols">
      <SessionsPanel />
      <PolicyPanel />
    </div>
  )
}

// ------------------------------------------------------------------------------------------------ sesiones activas

function SessionsPanel() {
  const t = useT()
  const f = useFormat()
  const sessions = useCompanySessions()
  const revoke = useRevokeCompanySession()
  const revokeOthers = useRevokeOtherSessions()
  const [target, setTarget] = useState<CompanySessionDto | null>(null)
  const [confirmOthers, setConfirmOthers] = useState(false)

  const rows = sessions.data ?? NO_SESSIONS
  const others = rows.filter((s) => !s.isCurrent).length

  const deviceText = useCallback(
    (s: CompanySessionDto) =>
      s.isDevice
        ? `${s.deviceName ?? t('system.audit.sessions.warehouseDevice')} (${t('system.audit.sessions.warehouseDevice')})`
        : (describeDevice(s.deviceInfo) ?? t('system.audit.sessions.unknownDevice')),
    [t],
  )

  const columns = useMemo<DataColumn<CompanySessionDto>[]>(
    () => [
      {
        id: 'user',
        header: t('system.audit.sessions.colUser'),
        card: 'title',
        sortValue: (s) => userText(s),
        cell: (s) => <span title={s.userEmail ?? undefined}>{userText(s)}</span>,
      },
      {
        id: 'device',
        header: t('system.audit.sessions.colDevice'),
        sortValue: (s) => deviceText(s),
        exportValue: (s) => deviceText(s),
        cell: (s) => (
          <span className="aud-device" title={s.deviceInfo ?? undefined}>
            {deviceText(s)}
            {s.isCurrent && <Chip tone="route">{t('system.audit.sessions.thisSession')}</Chip>}
          </span>
        ),
      },
      {
        id: 'location',
        header: t('system.audit.sessions.colLocation'),
        sortValue: (s) => sessionLocation(s) ?? '',
        cell: (s) => (
          <span className="aud-muted mono" title={t('system.audit.sessions.locationHint')}>
            {sessionLocation(s) ?? '—'}
          </span>
        ),
      },
      {
        id: 'lastActive',
        header: t('system.audit.sessions.colLastActive'),
        // la propia arriba en descendente, como la maqueta (`lastActiveSort` = hoy 23:59)
        sortValue: (s) => (s.isCurrent ? '9999-12-31' : (s.lastActivityUtc ?? '')),
        exportValue: (s) => s.lastActivityUtc ?? null,
        cell: (s) => <span className="mono aud-when">{s.isCurrent ? t('system.audit.sessions.rightNow') : f.dateTime(s.lastActivityUtc)}</span>,
      },
    ],
    [t, f, deviceText],
  )

  const actions = useMemo<RowAction<CompanySessionDto>[]>(
    () => [
      {
        key: 'revoke',
        label: t('system.audit.sessions.revokeBtn'),
        icon: <IconTrash />,
        tone: 'danger',
        perm: 'admin.users',
        visible: (s) => !s.isCurrent,
        disabled: () => revoke.isPending,
        onClick: (s) => setTarget(s),
      },
    ],
    [t, revoke.isPending],
  )

  return (
    <>
      <Panel
        flush
        icon={<IconShield />}
        title={t('system.audit.sessions.title')}
        badge={sessions.data ? rows.length : undefined}
        actions={
          <Can perm="admin.users">
            <button type="button" className="btn sm" disabled={others === 0 || revokeOthers.isPending} onClick={() => setConfirmOthers(true)}>
              <IconTrash /> {t('system.audit.sessions.revokeOthers')}
            </button>
          </Can>
        }
      >
        {sessions.isError ? (
          <EmptyState
            title={t('system.audit.sessions.loadError')}
            body={applyProblemDetails(sessions.error).title}
            action={
              <button type="button" className="btn" onClick={() => void sessions.refetch()}>
                {t('common.retry')}
              </button>
            }
          />
        ) : (
          <DataTable
            label={t('system.audit.sessions.title')}
            columns={columns}
            rows={rows}
            rowKey={(s) => s.id ?? 0}
            defaultSort={{ id: 'lastActive', desc: true }}
            rowActions={actions}
            loading={sessions.isPending}
            empty={<EmptyState title={t('system.audit.sessions.empty')} />}
          />
        )}
      </Panel>

      <ConfirmDialog
        open={target !== null}
        tone="danger"
        title={t('system.audit.sessions.revokeTitle')}
        message={t('system.audit.sessions.revokeBody', { user: target ? userText(target) : '', device: target ? deviceText(target) : '' })}
        confirmLabel={t('system.audit.sessions.revokeBtn')}
        onConfirm={async () => {
          if (target?.id == null) return
          await revoke.mutateAsync(target.id)
          toast.success(t('system.audit.sessions.revokedSessionMsg'))
        }}
        onClose={() => setTarget(null)}
      />
      <ConfirmDialog
        open={confirmOthers}
        tone="danger"
        title={t('system.audit.sessions.revokeOthersTitle')}
        message={t('system.audit.sessions.revokeOthersBody', { n: others })}
        confirmLabel={t('system.audit.sessions.revokeOthers')}
        onConfirm={async () => {
          const r = await revokeOthers.mutateAsync()
          toast.success(t('system.audit.sessions.revokedAllMsg', { n: r.revoked ?? 0 }))
        }}
        onClose={() => setConfirmOthers(false)}
      />
    </>
  )
}

// ------------------------------------------------------------------------------------------------ política

function PolicyPanel() {
  const t = useT()
  const settings = useTenantSettings()
  return (
    <Panel icon={<IconGear />} title={t('system.audit.policy.title')}>
      {settings.isPending ? (
        <Spinner block label={t('common.loading')} />
      ) : settings.isError || !settings.data ? (
        <EmptyState title={t('system.audit.policy.loadError')} body={settings.error ? applyProblemDetails(settings.error).title : undefined} />
      ) : (
        <PolicyForm settings={settings.data} />
      )}
    </Panel>
  )
}

function PolicyForm({ settings }: { settings: TenantSettingsDto }) {
  const t = useT()
  const canEdit = useCan('admin.tenant')
  const save = useSaveTenantSettings()
  const values = useMemo(() => toPolicyValues(settings), [settings])
  const form = useForm<PolicyValues>({ values })
  const dirty = form.formState.isDirty
  const minutes = reauthOptions(settings.aal2WindowMinutes).map((n) => ({ value: String(n), label: t('system.audit.policy.minutes', { n }) }))

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        const errors = policyErrors(v)
        const fields = Object.keys(errors) as (keyof PolicyValues)[]
        if (fields.length) {
          for (const k of fields) form.setError(k, { message: t(`system.audit.policy.${errors[k]}`) })
          return
        }
        const body = policyRequestBody(settings, v)
        if (Object.keys(body).length === 0) return
        // los 400 del servidor ('Entre 1 y 365 días.', …) los pone el Form bajo su campo
        await save.mutateAsync(body)
        toast.success(t('system.audit.policy.saved'))
      }}
    >
      {!canEdit && (
        <p className="note" role="status" style={{ marginBottom: 14 }}>
          {t('system.audit.policy.readOnly')}
        </p>
      )}
      <fieldset className="set-fs aud-policy" disabled={!canEdit}>
        <div className="aud-mfa">
          <Field name="mfaRequired" label={t('system.audit.policy.mfaRequiredLabel')} hideLabel>
            {/* el texto visible junto al interruptor (maqueta) no se repite en el nombre accesible: lo da la etiqueta del campo */}
            <Toggle text={<span aria-hidden="true">{t('system.audit.policy.mfaRequiredLabel')}</span>} aria-describedby="aud-mfa-hint" />
          </Field>
        </div>
        <p id="aud-mfa-hint" className="set-d">
          {t('system.audit.policy.mfaRequiredHint')}
        </p>
        <Field name="aal2WindowMinutes" label={t('system.audit.policy.reauthLabel')} help={t('system.audit.policy.reauthHint')}>
          <Select options={minutes} />
        </Field>
        <div className="r2" style={{ marginTop: 16 }}>
          <Field name="sessionDays" label={t('system.audit.policy.sessionDaysLabel')}>
            <NumberInput className="mono" min={1} max={365} step={1} inputMode="numeric" />
          </Field>
          <Field name="deviceSessionDays" label={t('system.audit.policy.deviceDaysLabel')}>
            <NumberInput className="mono" min={1} max={365} step={1} inputMode="numeric" />
          </Field>
        </div>
        <p className="set-d">{t('system.audit.policy.sessionDaysHint')}</p>
      </fieldset>
      {canEdit && (
        <div className="set-actions">
          {dirty && <p className="set-d">{t('system.audit.policy.unsaved')}</p>}
          <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset(values)}>
            {t('system.audit.policy.discard')}
          </button>
          <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('system.audit.policy.saving') : t('system.audit.policy.save')}
          </button>
        </div>
      )}
    </Form>
  )
}
