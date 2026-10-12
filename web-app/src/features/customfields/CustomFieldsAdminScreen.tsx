// Lote F-F1 — Campos personalizados (`/analytics/custom-fields`, admin.customfields + CUSTOM_FIELDS). Administra las DEFINICIONES de
// los campos propios de cada entidad (maqueta `camposScreen`): panel de entidades con contador y, de la elegida, su tabla con
// «Nuevo campo», editar, desactivar y reactivar. Los valores de cada registro los pinta y guarda `CustomFieldsForm` en cada ficha.
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import {
  CUSTOM_FIELD_ENTITY_TYPES,
  useCustomFieldDefinitionsAdmin,
  useSetCustomFieldDefinitionActive,
  type CustomFieldDefinitionDto,
  type CustomFieldEntityType,
} from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, EmptyState, IconEdit, IconPower, IconRotateCcw, Panel, Spinner, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconCheck } from '../../kernel/ui/icons'
import { IconTag } from '../../kernel/ui/screenIcons'
import { useCatalogDomains } from '../system/api'
import { CustomFieldDefinitionModal } from './CustomFieldDefinitionModal'
import './customFieldsAdmin.css'

const PERM = 'admin.customfields'
const NO_ROWS: CustomFieldDefinitionDto[] = []

/** «Lista: Prioridad · 3 opciones» (lista del catálogo) u «Opciones propias · 2» (definiciones hechas por API). */
function TypeCell({ def, listNames }: { def: CustomFieldDefinitionDto; listNames: Map<string, string> }) {
  const t = useT()
  const { data: values } = useLookups(def.refEntity || null)
  const type = (def.dataType ?? '').toUpperCase()
  let info: string | null = null
  if (def.refEntity) {
    info = values ? t('customFieldsAdmin.listInfo', { name: listNames.get(def.refEntity) ?? def.refEntity, n: values.length }) : (listNames.get(def.refEntity) ?? def.refEntity)
  } else {
    const own = (def.options ?? []).filter((o) => o.isActive !== false).length
    if (own > 0) info = t('customFieldsAdmin.ownOptionsInfo', { n: own })
  }
  return (
    <div>
      <div>{t(`customFieldsAdmin.types.${type}`)}</div>
      {info && (
        <div className="meta cf-type-info">
          <IconTag /> {info}
        </div>
      )}
    </div>
  )
}

const check = (on: boolean | undefined, label: string) =>
  on ? (
    <span className="cf-yes" role="img" aria-label={label}>
      <IconCheck />
    </span>
  ) : (
    '—'
  )

export default function CustomFieldsAdminScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [includeInactive, setIncludeInactive] = useState(false)
  const [modal, setModal] = useState<{ def: CustomFieldDefinitionDto | null } | null>(null)
  const [deactivating, setDeactivating] = useState<CustomFieldDefinitionDto | null>(null)
  const { data, isLoading, error } = useCustomFieldDefinitionsAdmin(null, true)
  const { data: domains } = useCatalogDomains()
  const setActive = useSetCustomFieldDefinitionActive()

  const requested = params.get('entity')
  const entity: CustomFieldEntityType = (CUSTOM_FIELD_ENTITY_TYPES as readonly string[]).includes(requested ?? '') ? (requested as CustomFieldEntityType) : CUSTOM_FIELD_ENTITY_TYPES[0]
  const entityLabel = (code: string) => t(`customFieldsAdmin.entityNames.${code}`)
  const listNames = useMemo(() => new Map((domains ?? []).map((d) => [d.domainKey ?? '', d.label ?? d.domainKey ?? ''])), [domains])

  // Contador por entidad: solo campos activos (los desactivados no se piden ni se muestran en las fichas).
  const counts = useMemo(() => {
    const map = new Map<string, number>()
    for (const d of data ?? NO_ROWS) if (d.isActive !== false) map.set(d.entityType ?? '', (map.get(d.entityType ?? '') ?? 0) + 1)
    return map
  }, [data])

  const rows = useMemo(
    () =>
      (data ?? NO_ROWS)
        .filter((d) => d.entityType === entity && (includeInactive || d.isActive !== false))
        .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || (a.fieldKey ?? '').localeCompare(b.fieldKey ?? '')),
    [data, entity, includeInactive],
  )

  const columns = useMemo<DataColumn<CustomFieldDefinitionDto>[]>(
    () => [
      { id: 'label', header: t('customFieldsAdmin.columns.label'), cell: (r) => r.label || r.fieldKey, sortValue: (r) => r.label || r.fieldKey || '', card: 'title' },
      { id: 'key', header: t('customFieldsAdmin.columns.key'), cell: (r) => <span className="mono">{r.fieldKey}</span>, sortValue: (r) => r.fieldKey ?? '' },
      {
        id: 'type',
        header: t('customFieldsAdmin.columns.type'),
        cell: (r) => <TypeCell def={r} listNames={listNames} />,
        sortValue: (r) => t(`customFieldsAdmin.types.${(r.dataType ?? '').toUpperCase()}`),
        exportValue: (r) => t(`customFieldsAdmin.types.${(r.dataType ?? '').toUpperCase()}`),
      },
      {
        id: 'required',
        header: t('customFieldsAdmin.columns.required'),
        cell: (r) => check(r.isRequired, t('customFieldsAdmin.columns.required')),
        sortValue: (r) => (r.isRequired ? 1 : 0),
        exportValue: (r) => (r.isRequired ? t('customFields.yes') : t('customFields.no')),
      },
      {
        id: 'unique',
        header: t('customFieldsAdmin.columns.unique'),
        cell: (r) => check(r.isUnique, t('customFieldsAdmin.columns.unique')),
        sortValue: (r) => (r.isUnique ? 1 : 0),
        exportValue: (r) => (r.isUnique ? t('customFields.yes') : t('customFields.no')),
      },
      {
        id: 'showInList',
        header: t('customFieldsAdmin.columns.showInList'),
        cell: (r) => check(r.showInList, t('customFieldsAdmin.columns.showInList')),
        sortValue: (r) => (r.showInList ? 1 : 0),
        exportValue: (r) => (r.showInList ? t('customFields.yes') : t('customFields.no')),
      },
      {
        id: 'status',
        header: t('customFieldsAdmin.columns.status'),
        cell: (r) => (r.isActive === false ? <Chip tone="fail">{t('customFieldsAdmin.columns.inactive')}</Chip> : <Chip tone="deliv">{t('customFieldsAdmin.columns.active')}</Chip>),
        sortValue: (r) => (r.isActive === false ? 0 : 1),
        exportValue: (r) => t(r.isActive === false ? 'customFieldsAdmin.columns.inactive' : 'customFieldsAdmin.columns.active'),
      },
    ],
    [t, listNames],
  )

  const actions = useMemo<RowAction<CustomFieldDefinitionDto>[]>(
    () => [
      { key: 'edit', label: t('customFieldsAdmin.actions.edit'), perm: PERM, icon: <IconEdit />, visible: (r) => r.isActive !== false, onClick: (r) => setModal({ def: r }) },
      { key: 'deactivate', label: t('customFieldsAdmin.actions.deactivate'), perm: PERM, tone: 'danger', icon: <IconPower />, visible: (r) => r.isActive !== false, onClick: (r) => setDeactivating(r) },
      {
        key: 'reactivate',
        label: t('customFieldsAdmin.actions.reactivate'),
        perm: PERM,
        icon: <IconRotateCcw />,
        visible: (r) => r.isActive === false,
        onClick: async (r) => {
          try {
            await setActive.mutateAsync({ id: r.id ?? 0, active: true })
            toast.success(t('customFieldsAdmin.toast.reactivated', { label: r.label || r.fieldKey || '' }))
          } catch (err) {
            toast.error(applyProblemDetails(err).title)
          }
        },
      },
    ],
    [t, setActive],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('customFieldsAdmin.title')}</h1>
          <p>{t('customFieldsAdmin.subtitle')}</p>
        </div>
      </div>

      <div className="cf-cols">
        <Panel flush icon={<IconTag />} title={t('customFieldsAdmin.entities')}>
          <nav aria-label={t('customFieldsAdmin.entities')}>
            {CUSTOM_FIELD_ENTITY_TYPES.map((code) => (
              <button
                key={code}
                type="button"
                className={code === entity ? 'unrow on' : 'unrow'}
                aria-current={code === entity ? 'true' : undefined}
                onClick={() => setParams(code === CUSTOM_FIELD_ENTITY_TYPES[0] ? {} : { entity: code }, { replace: true })}
              >
                <span className="cf-entity-name">{entityLabel(code)}</span>
                <span className="meta" aria-label={String(counts.get(code) ?? 0)}>
                  {counts.get(code) ?? 0}
                </span>
              </button>
            ))}
          </nav>
        </Panel>

        <Panel
          flush
          icon={<IconTag />}
          title={entityLabel(entity)}
          actions={
            <>
              <label className="sw cf-inactive-switch">
                <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('customFieldsAdmin.includeInactive')}</span>
              </label>
              <Can perm={PERM}>
                <button type="button" className="btn sm flow" onClick={() => setModal({ def: null })}>
                  + {t('customFieldsAdmin.newField')}
                </button>
              </Can>
            </>
          }
        >
          {isLoading ? (
            <Spinner block label={t('common.loading')} />
          ) : error ? (
            <p className="pb ferr" role="alert">
              {t('customFieldsAdmin.loadError')} {applyProblemDetails(error).title}
            </p>
          ) : (
            <DataTable
              label={entityLabel(entity)}
              columns={columns}
              rows={rows}
              rowKey={(r) => String(r.id)}
              rowActions={actions}
              rowClassName={(r) => (r.isActive === false ? 'dim' : undefined)}
              exportFileName={`${t('customFieldsAdmin.title')} - ${entityLabel(entity)}`}
              empty={<EmptyState title={t('customFieldsAdmin.noFields')} />}
            />
          )}
        </Panel>
      </div>

      <div className="note cf-note">{t('customFieldsAdmin.note')}</div>

      {modal && (
        <CustomFieldDefinitionModal
          key={modal.def?.id ?? 'new'}
          entityType={modal.def?.entityType ?? entity}
          entityLabel={entityLabel(modal.def?.entityType ?? entity)}
          definition={modal.def}
          onClose={() => setModal(null)}
        />
      )}

      <ConfirmDialog
        open={deactivating !== null}
        tone="danger"
        title={t('customFieldsAdmin.deactivateTitle')}
        message={t('customFieldsAdmin.deactivateBody', { label: deactivating?.label || deactivating?.fieldKey || '' })}
        confirmLabel={t('customFieldsAdmin.actions.deactivate')}
        onConfirm={async () => {
          if (!deactivating) return
          await setActive.mutateAsync({ id: deactivating.id ?? 0, active: false })
          toast.success(t('customFieldsAdmin.toast.deactivated', { label: deactivating.label || deactivating.fieldKey || '' }))
        }}
        onClose={() => setDeactivating(null)}
      />
    </div>
  )
}
