// IP preferida / red de confianza (2026-10-12; docs/ip-preferida-mfa-diseno.md §5): Seguridad y auditoría → Sesiones y MFA, debajo de la política de MFA. La
// lista de IPs o rangos de la compañía desde los que NO se pide el código MFA al iniciar sesión (la contraseña sí; las acciones sensibles siguen pidiendo
// MFA). Solo `admin.tenant`; alta, cambio, baja y máximo exigen MFA reciente (el cliente abre solo el modal de reautenticación). Muestra «Su IP actual es X»
// con «Agregar esta IP», el aviso permanente de que toda persona de esa red entra sin código, el máximo de entradas de la compañía y «Probar una IP».
import { useMemo, useState } from 'react'
import { useCan } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/client'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, EmptyState, IconPencil, IconPower, IconShield, IconTrash, Panel, Spinner, toast, type DataColumn, type RowAction } from '../../../kernel/ui'
import {
  useDeleteTrustedNetwork,
  useSetTrustedNetworkLimit,
  useTrustedNetworks,
  useUpdateTrustedNetwork,
  type TrustedNetworkDto,
} from '../trustedNetworksApi'
import { TrustedNetworkModal } from './TrustedNetworkModal'
import { cidrContains, formatCount, parseCidr, parseIp } from './trustedNetworkCidr'
import './trustedNetworks.css'

const NO_ITEMS: TrustedNetworkDto[] = []

export function TrustedNetworksPanel() {
  // la lista es de quien administra la compañía: sin admin.tenant no se pinta (el servidor tampoco la entrega)
  if (!useCan('admin.tenant')) return null
  return <PanelBody />
}

function PanelBody() {
  const t = useT()
  const f = useFormat()
  const data = useTrustedNetworks()
  const update = useUpdateTrustedNetwork()
  const remove = useDeleteTrustedNetwork()
  const setLimit = useSetTrustedNetworkLimit()
  const [modal, setModal] = useState<{ entry: TrustedNetworkDto | null; initialCidr?: string } | null>(null)
  const [deleting, setDeleting] = useState<TrustedNetworkDto | null>(null)
  const [limitText, setLimitText] = useState<string | null>(null)
  const [testText, setTestText] = useState('')
  const [testResult, setTestResult] = useState<'covered' | 'notCovered' | 'invalid' | null>(null)
  const [testName, setTestName] = useState('')

  const dto = data.data
  const items = dto?.items ?? NO_ITEMS
  const max = dto?.maxEntries ?? null
  const limitShown = limitText ?? (max === null ? '' : String(max))
  const limitDirty = dto !== undefined && limitText !== null && limitText.trim() !== (max === null ? '' : String(max))

  const state = (n: TrustedNetworkDto): 'active' | 'inactive' | 'expired' => (n.isExpired ? 'expired' : n.isActive ? 'active' : 'inactive')

  const columns = useMemo<DataColumn<TrustedNetworkDto>[]>(
    () => [
      {
        id: 'name',
        header: t('system.audit.trusted.colName'),
        card: 'title',
        sortValue: (n) => n.name ?? '',
        cell: (n) => (
          <span className="trn-name">
            {n.name}
            {n.containsCurrentIp && <Chip tone="route">{t('system.audit.trusted.here')}</Chip>}
          </span>
        ),
      },
      {
        id: 'cidr',
        header: t('system.audit.trusted.colRange'),
        sortValue: (n) => n.cidr ?? '',
        cell: (n) => <span className="mono">{n.cidr}</span>,
      },
      {
        id: 'count',
        header: t('system.audit.trusted.colAddresses'),
        sortValue: (n) => Number(n.addressCount ?? 0),
        exportValue: (n) => n.addressCount ?? '',
        cell: (n) => <span className="mono">{formatCount(BigInt(n.addressCount ?? '0'), f.lang)}</span>,
      },
      {
        id: 'state',
        header: t('system.audit.trusted.colState'),
        sortValue: (n) => state(n),
        exportValue: (n) => t(`system.audit.trusted.state.${state(n)}`),
        cell: (n) => <Chip tone={state(n) === 'active' ? 'deliv' : state(n) === 'expired' ? 'fail' : 'cap'}>{t(`system.audit.trusted.state.${state(n)}`)}</Chip>,
      },
      {
        id: 'expires',
        header: t('system.audit.trusted.colExpires'),
        sortValue: (n) => n.expiresAtUtc ?? '9999',
        exportValue: (n) => n.expiresAtUtc ?? '',
        cell: (n) => <span className="mono aud-when">{n.expiresAtUtc ? f.date(n.expiresAtUtc) : '—'}</span>,
      },
      {
        id: 'confirmed',
        header: t('system.audit.trusted.colConfirmed'),
        sortValue: (n) => n.internalOnlyConfirmedAtUtc ?? '',
        exportValue: (n) => [n.internalOnlyConfirmedByName, n.internalOnlyConfirmedAtUtc].filter(Boolean).join(' · '),
        cell: (n) => (
          <span className="aud-muted" title={t('system.audit.trusted.confirmedHint')}>
            {n.internalOnlyConfirmedAtUtc ? `${n.internalOnlyConfirmedByName ?? ''} · ${f.date(n.internalOnlyConfirmedAtUtc)}` : '—'}
          </span>
        ),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f],
  )

  const actions = useMemo<RowAction<TrustedNetworkDto>[]>(
    () => [
      { key: 'edit', label: t('system.audit.trusted.edit'), icon: <IconPencil />, perm: 'admin.tenant', onClick: (n) => setModal({ entry: n }) },
      {
        key: 'deactivate',
        label: t('system.audit.trusted.deactivate'),
        icon: <IconPower />,
        perm: 'admin.tenant',
        visible: (n) => Boolean(n.isActive),
        disabled: () => update.isPending,
        onClick: (n) => void toggle(n, false),
      },
      {
        key: 'activate',
        label: t('system.audit.trusted.activate'),
        icon: <IconPower />,
        tone: 'flow',
        perm: 'admin.tenant',
        visible: (n) => !n.isActive,
        disabled: () => update.isPending,
        onClick: (n) => void toggle(n, true),
      },
      { key: 'delete', label: t('system.audit.trusted.delete'), icon: <IconTrash />, tone: 'danger', perm: 'admin.tenant', onClick: (n) => setDeleting(n) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, update.isPending],
  )

  async function toggle(n: TrustedNetworkDto, active: boolean) {
    try {
      await update.mutateAsync({ publicId: n.publicId ?? '', body: { isActive: active } })
      toast.success(t(active ? 'system.audit.trusted.activated' : 'system.audit.trusted.deactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  async function saveLimit() {
    const text = limitShown.trim()
    const value = text === '' ? null : Number(text)
    if (value !== null && (!Number.isInteger(value) || value < 0)) return toast.error(t('system.audit.trusted.limitInvalid'))
    try {
      await setLimit.mutateAsync(value)
      setLimitText(null)
      toast.success(t('system.audit.trusted.limitSaved'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  function runTest() {
    const ip = parseIp(testText)
    if (!ip) {
      setTestResult('invalid')
      return
    }
    // solo cuentan las entradas activas y vigentes (igual que el login)
    const hit = items.find((n) => {
      if (!n.isActive || n.isExpired) return false
      const c = parseCidr(n.cidr ?? '').cidr
      return c !== undefined && cidrContains(c, ip)
    })
    setTestName(hit?.name ?? '')
    setTestResult(hit ? 'covered' : 'notCovered')
  }

  return (
    <>
      <Panel
        icon={<IconShield />}
        title={t('system.audit.trusted.title')}
        badge={dto ? items.length : undefined}
        actions={
          <button type="button" className="btn sm flow" onClick={() => setModal({ entry: null })} disabled={!dto}>
            {t('system.audit.trusted.add')}
          </button>
        }
      >
        {data.isPending ? (
          <Spinner block label={t('common.loading')} />
        ) : data.isError || !dto ? (
          <EmptyState
            title={t('system.audit.trusted.loadError')}
            body={data.error ? applyProblemDetails(data.error).title : undefined}
            action={
              <button type="button" className="btn" onClick={() => void data.refetch()}>
                {t('common.retry')}
              </button>
            }
          />
        ) : (
          <div className="trn">
            <p className="set-d">{t('system.audit.trusted.intro')}</p>
            <p className="trn-warn" role="note">
              {t('system.audit.trusted.warning')}
            </p>
            {!dto.featureEnabled && (
              <p className="alert" role="status">
                {t('system.audit.trusted.featureOff')}
              </p>
            )}

            <div className="trn-tools">
              <div className="trn-ip">
                {dto.currentIp ? (
                  <>
                    <p>
                      {t('system.audit.trusted.currentIp')} <strong className="mono">{dto.currentIp}</strong>
                      {dto.currentIpNetworkName ? ` — ${t('system.audit.trusted.covered', { name: dto.currentIpNetworkName })}` : ` — ${t('system.audit.trusted.notCovered')}`}
                    </p>
                    {!dto.currentIpNetworkName && (
                      <button type="button" className="btn sm" onClick={() => setModal({ entry: null, initialCidr: dto.currentIp ?? '' })}>
                        {t('system.audit.trusted.addCurrent')}
                      </button>
                    )}
                  </>
                ) : (
                  <p className="aud-muted">{t('system.audit.trusted.noCurrentIp')}</p>
                )}
              </div>

              <div className="f trn-limit">
                <label htmlFor="trn-limit">{t('system.audit.trusted.limitLabel')}</label>
                <div className="trn-limit-row">
                  <input id="trn-limit" type="number" min={0} step={1} inputMode="numeric" className="mono" value={limitShown} placeholder={t('system.audit.trusted.limitNone')} onChange={(e) => setLimitText(e.target.value)} />
                  <button type="button" className="btn sm" disabled={!limitDirty || setLimit.isPending} onClick={() => void saveLimit()}>
                    {t('system.audit.trusted.limitSave')}
                  </button>
                </div>
                <p className="set-d">{max === null ? t('system.audit.trusted.limitUsageNone', { used: items.length }) : t('system.audit.trusted.limitUsage', { used: items.length, max })}</p>
              </div>

              <div className="f trn-test">
                <label htmlFor="trn-test">{t('system.audit.trusted.testLabel')}</label>
                <div className="trn-limit-row">
                  <input id="trn-test" className="mono" value={testText} placeholder="203.0.113.25" autoComplete="off" spellCheck={false} onChange={(e) => { setTestText(e.target.value); setTestResult(null) }} onKeyDown={(e) => { if (e.key === 'Enter') runTest() }} />
                  <button type="button" className="btn sm" onClick={runTest} disabled={!testText.trim()}>
                    {t('system.audit.trusted.testRun')}
                  </button>
                </div>
                {testResult && (
                  <p className={testResult === 'covered' ? 'trn-ok' : 'set-d'} role="status">
                    {testResult === 'covered' ? t('system.audit.trusted.testCovered', { name: testName }) : testResult === 'notCovered' ? t('system.audit.trusted.testNotCovered') : t('system.audit.trusted.testInvalid')}
                  </p>
                )}
              </div>
            </div>

            <DataTable
              label={t('system.audit.trusted.title')}
              columns={columns}
              rows={items}
              rowKey={(n) => n.publicId ?? ''}
              defaultSort={{ id: 'name', desc: false }}
              rowActions={actions}
              empty={<EmptyState title={t('system.audit.trusted.empty')} body={t('system.audit.trusted.emptyBody')} />}
            />
          </div>
        )}
      </Panel>

      <TrustedNetworkModal open={modal !== null} entry={modal?.entry ?? null} initialCidr={modal?.initialCidr} currentIp={dto?.currentIp} onClose={() => setModal(null)} />
      <ConfirmDialog
        open={deleting !== null}
        tone="danger"
        title={t('system.audit.trusted.deleteTitle')}
        message={t('system.audit.trusted.deleteBody', { name: deleting?.name ?? '', range: deleting?.cidr ?? '' })}
        confirmLabel={t('system.audit.trusted.delete')}
        onConfirm={async () => {
          if (!deleting?.publicId) return
          await remove.mutateAsync(deleting.publicId)
          toast.success(t('system.audit.trusted.deleted'))
        }}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}
