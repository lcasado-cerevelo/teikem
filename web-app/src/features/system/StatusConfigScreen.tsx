// Sistema → Estatus (2026-10-21, pedido de Luis: «no hay ningún sitio donde yo controle eso»): las etapas de cada dominio de estatus con su orden, su tipo y si la compañía las usa.
// Una etapa se puede apagar (no se ofrece más), subir o bajar (orden propio) y renombrar (español e inglés). Al guardar, el servidor valida el flujo completo (una etapa inicial, y terminal o de pipeline solo
// si el dominio las define) y responde con el motivo si quedaría inválido. Lee `catalogs.view`; cambia `admin.statusconfig`. Sin ítem de menú: se abre desde Catálogos.
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Panel, Spinner, toast, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import { useCatalogDomains } from './api'
import { swapOrder, useStatusOverride, useStatusStages, useStatusValidation, type StatusStage } from './statusConfigApi'

const STAGE_TONE: Record<string, ChipTone> = { PIPELINE: 'deliv', LATERAL: 'warn', TERMINAL: 'cap' }
const STATUS_SCOPE = 2

function RenameModal({ domain, stage, onClose }: { domain: string; stage: StatusStage; onClose: () => void }) {
  const t = useT()
  const override = useStatusOverride(domain)
  const [es, setEs] = useState(stage.labels?.es ?? stage.label ?? '')
  const [en, setEn] = useState(stage.labels?.en ?? '')
  const [error, setError] = useState<string | null>(null)
  const save = async () => {
    if (!es.trim()) return setError(t('system.statuses.nameRequired'))
    try {
      await override.mutateAsync({ code: stage.code ?? '', patch: { labels: { es: es.trim(), ...(en.trim() ? { en: en.trim() } : {}) } } })
      toast.success(t('system.statuses.renamed'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title ?? '')
    }
  }
  return (
    <Modal
      open
      size="sm"
      title={t('system.statuses.renameTitle', { code: stage.code ?? '' })}
      onClose={onClose}
      dismissible={!override.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={override.isPending}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={override.isPending}>
            {t('system.statuses.save')}
          </button>
        </>
      }
    >
      <div className="f">
        <label htmlFor="st-es">{t('system.statuses.nameEs')}</label>
        <input id="st-es" value={es} maxLength={80} onChange={(e) => setEs(e.target.value)} autoComplete="off" />
      </div>
      <div className="f">
        <label htmlFor="st-en">{t('system.statuses.nameEn')}</label>
        <input id="st-en" value={en} maxLength={80} onChange={(e) => setEn(e.target.value)} autoComplete="off" />
      </div>
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}

export default function StatusConfigScreen() {
  const t = useT()
  const domains = useCatalogDomains()
  const statusDomains = useMemo(() => (domains.data ?? []).filter((d) => d.scope === STATUS_SCOPE).sort((a, b) => (a.label ?? '').localeCompare(b.label ?? '')), [domains.data])
  const [picked, setPicked] = useState<string | null>(null)
  const domain = picked ?? statusDomains.find((d) => d.domainKey === 'ClientStatus')?.domainKey ?? statusDomains[0]?.domainKey ?? null
  const stages = useStatusStages(domain)
  const validation = useStatusValidation(domain)
  const override = useStatusOverride(domain)
  const [renaming, setRenaming] = useState<StatusStage | null>(null)
  const rows = useMemo(() => stages.data ?? [], [stages.data])

  const run = async (code: string, patch: Parameters<typeof override.mutateAsync>[0]['patch']) => {
    try {
      await override.mutateAsync({ code, patch })
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const move = async (index: number, direction: -1 | 1) => {
    const swap = swapOrder(rows, index, direction)
    if (!swap) return
    try {
      for (const [code, order] of swap) await override.mutateAsync({ code, patch: { sortOverride: order } })
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<StatusStage>[]>(
    () => [
      { id: 'order', header: t('system.statuses.col.order'), align: 'end', cell: (s) => <span className="mono">{s.sortOrder}</span>, sortValue: (s) => s.sortOrder ?? 0 },
      { id: 'code', header: t('system.statuses.col.code'), cell: (s) => <span className="ref">{s.code}</span>, sortValue: (s) => s.code ?? '', card: 'title' },
      {
        id: 'label',
        header: t('system.statuses.col.name'),
        cell: (s) => (
          <>
            {s.label}
            {s.isOverridden && <span className="help"> · {t('system.statuses.custom')}</span>}
          </>
        ),
        sortValue: (s) => s.label ?? '',
      },
      {
        id: 'kind',
        header: t('system.statuses.col.kind'),
        cell: (s) => (
          <>
            <Chip tone={STAGE_TONE[(s.stageKind ?? '').toUpperCase()] ?? 'neutral'}>{t(`system.statuses.kind.${(s.stageKind ?? '').toUpperCase()}`)}</Chip>
            {s.isInitial && <Chip tone="neutral">{t('system.statuses.initial')}</Chip>}
          </>
        ),
        sortValue: (s) => s.stageKind ?? '',
      },
      {
        id: 'enabled',
        header: t('system.statuses.col.enabled'),
        cell: (s) => (
          <Can perm="admin.statusconfig" fallback={<Chip tone={s.isEnabled ? 'deliv' : 'warn'}>{t(s.isEnabled ? 'system.catalogs.yes' : 'system.catalogs.no')}</Chip>}>
            <label className="sw">
              <input type="checkbox" role="switch" aria-label={t('system.statuses.enabledOf', { code: s.code ?? '' })} checked={!!s.isEnabled} disabled={override.isPending} onChange={(e) => void run(s.code ?? '', { isEnabled: e.target.checked })} />
              <span className="tk" aria-hidden="true" />
            </label>
          </Can>
        ),
        sortValue: (s) => (s.isEnabled ? 1 : 0),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, override.isPending],
  )

  const errors = validation.data?.errors ?? []
  const warnings = validation.data?.warnings ?? []

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('system.statuses.title')}</h1>
          <p>{t('system.statuses.subtitle')}</p>
        </div>
      </div>
      <div className="f" style={{ maxWidth: 420 }}>
        <label htmlFor="st-domain">{t('system.statuses.domain')}</label>
        <select id="st-domain" value={domain ?? ''} onChange={(e) => setPicked(e.target.value)}>
          {statusDomains.map((d) => (
            <option key={d.domainKey} value={d.domainKey}>
              {d.label}
            </option>
          ))}
        </select>
      </div>
      <p className="help">{t('system.statuses.help')}</p>
      {errors.length > 0 && (
        <div className="note" role="alert">
          <b>{t('system.statuses.invalid')}</b> {errors.join(' ')}
        </div>
      )}
      {errors.length === 0 && warnings.length > 0 && <p className="help">{warnings.join(' ')}</p>}
      <Panel flush icon={<IconTag />} title={t('system.statuses.stages')} badge={stages.isPending ? undefined : rows.length}>
        <div className="pb">
          {stages.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(stages.error).title}
            </p>
          ) : stages.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('system.statuses.stages')}
              columns={columns}
              rows={rows}
              rowKey={(s) => s.code ?? String(s.id)}
              defaultSort={{ id: 'order', desc: false }}
              empty={t('system.statuses.empty')}
              rowActions={[
                { key: 'up', label: t('system.statuses.up'), perm: 'admin.statusconfig', onClick: (s) => void move(rows.findIndex((r) => r.code === s.code), -1), disabled: () => override.isPending },
                { key: 'down', label: t('system.statuses.down'), perm: 'admin.statusconfig', onClick: (s) => void move(rows.findIndex((r) => r.code === s.code), 1), disabled: () => override.isPending },
                { key: 'rename', label: t('system.statuses.rename'), perm: 'admin.statusconfig', onClick: (s) => setRenaming(s) },
              ]}
            />
          )}
        </div>
      </Panel>
      {renaming && domain && <RenameModal domain={domain} stage={renaming} onClose={() => setRenaming(null)} />}
    </div>
  )
}
