// Ajustes → Módulos (maqueta `tenantModulesPanelHtml`): catálogo de módulos de la plataforma (`GET /modules`) con su estado en la
// compañía, dependencias ("Requiere: …") y núcleos bloqueados. Encender exige el módulo requerido; apagar avisa qué otros se
// apagan en cascada. `PUT /modules/{key}` exige `admin.tenant` y AAL2: el cliente abre la reautenticación del kit ante el 403
// `aal2_required` y reintenta solo. Al cambiar, se vuelve a pedir `me` para que el menú muestre u oculte las pantallas.
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { ME_QUERY_KEY } from '../../../app/session'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, EmptyState, IconLayers, Panel, toast, type DataColumn } from '../../../kernel/ui'
import { problemText } from '../../warehouse/problemText'
import { dependencyOff, enabledDependents, moduleName } from '../tenantModules'
import { useModulesCatalog, useSetModule, type ModuleDto } from '../tenantSettingsApi'

const NO_MODULES: ModuleDto[] = []

export function ModulesTab({ canEdit }: { canEdit: boolean }) {
  const t = useT()
  const qc = useQueryClient()
  const query = useModulesCatalog()
  const setModule = useSetModule()
  const modules = query.data ?? NO_MODULES
  const [turningOff, setTurningOff] = useState<ModuleDto | null>(null)

  const apply = async (m: ModuleDto, enabled: boolean) => {
    await setModule.mutateAsync({ key: m.key ?? '', enabled })
    await qc.invalidateQueries({ queryKey: ME_QUERY_KEY })
    toast.success(t(enabled ? 'system.settings.modules.turnedOn' : 'system.settings.modules.turnedOff', { name: m.name ?? m.key ?? '' }))
  }

  const change = (m: ModuleDto, enabled: boolean) => {
    if (!enabled && enabledDependents(m.key ?? '', modules).length > 0) {
      setTurningOff(m)
      return
    }
    apply(m, enabled).catch((err) => toast.error(problemText(err)))
  }

  const columns = useMemo<DataColumn<ModuleDto>[]>(
    () => [
      {
        id: 'module',
        header: t('system.settings.modules.colModule'),
        card: 'title',
        sortValue: (m) => m.name ?? m.key,
        exportValue: (m) => m.name ?? m.key,
        cell: (m) => (
          <div>
            <b style={{ fontSize: 13 }}>{m.name ?? m.key}</b>
            {m.dependsOn && (
              <div className="set-sub">
                {t('system.settings.modules.dependsOnPrefix')} {moduleName(m.dependsOn, modules)}
              </div>
            )}
          </div>
        ),
      },
      {
        id: 'status',
        header: t('system.settings.modules.colStatus'),
        sortValue: (m) => (m.isEnabled ? t('system.settings.modules.statusOn') : t('system.settings.modules.statusOff')),
        cell: (m) => (
          <Chip tone={m.isEnabled ? 'deliv' : 'cap'}>{m.isEnabled ? t('system.settings.modules.statusOn') : t('system.settings.modules.statusOff')}</Chip>
        ),
      },
      {
        id: 'action',
        header: t('system.settings.modules.colAction'),
        align: 'end',
        exportable: false,
        cell: (m) => {
          if (m.isCore) return <span className="set-sub">{t('system.settings.modules.coreLabel')}</span>
          if (!canEdit) return null
          const blocked = !m.isEnabled && dependencyOff(m, modules)
          const why = blocked ? t('system.settings.modules.needDependency', { name: moduleName(m.dependsOn, modules) }) : undefined
          return (
            <label className="sw" title={why}>
              <input
                type="checkbox"
                role="switch"
                checked={m.isEnabled ?? false}
                disabled={blocked || setModule.isPending}
                aria-label={t('system.settings.modules.toggle', { name: m.name ?? m.key ?? '' })}
                aria-description={why}
                onChange={(e) => change(m, e.target.checked)}
              />
              <span className="tk" aria-hidden="true" />
            </label>
          )
        },
      },
    ],
    // `change` cierra sobre `modules`; se recalcula con ellos
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, modules, canEdit, setModule.isPending],
  )

  if (query.isError) return <EmptyState title={t('system.settings.modules.loadError')} body={applyProblemDetails(query.error).title} />

  const cascade = turningOff ? enabledDependents(turningOff.key ?? '', modules) : []

  return (
    <>
      <Panel flush icon={<IconLayers />} title={t('system.settings.modules.modulesTitle')} badge={query.data ? modules.length : undefined}>
        <DataTable
          columns={columns}
          rows={modules}
          rowKey={(m) => m.key ?? ''}
          defaultSort={{ id: 'module', desc: false }}
          loading={query.isPending}
          label={t('system.settings.modules.modulesTitle')}
        />
        <p className="note" style={{ margin: 0, border: 'none', borderRadius: 0 }}>
          {t('system.settings.modules.modulesHint')}
        </p>
      </Panel>
      <ConfirmDialog
        open={turningOff !== null}
        title={t('system.settings.modules.confirmOffTitle', { name: turningOff?.name ?? '' })}
        message={t('system.settings.modules.confirmOffMessage', {
          name: turningOff?.name ?? '',
          list: cascade.map((m) => m.name ?? m.key).join(', '),
        })}
        confirmLabel={t('system.settings.modules.confirmOff')}
        tone="danger"
        onConfirm={async () => {
          if (!turningOff) return
          await apply(turningOff, false)
          setTurningOff(null)
        }}
        onClose={() => setTurningOff(null)}
      />
    </>
  )
}
