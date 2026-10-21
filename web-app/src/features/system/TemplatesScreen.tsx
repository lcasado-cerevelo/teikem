// Plantillas (desde el botón de Integraciones; dictado de Luis, 2026-10-19): ver, crear, editar, desactivar y reactivar las plantillas de importación sin pasar por el asistente
// de Importar. Una pestaña por entidad (envíos y consignatarios por ahora); se reutilizan los mismos editores que ya usan sus asistentes.
// Entrar: integrations.view (las plantillas son lo que usan las integraciones); cambiar: el permiso de cada entidad (TEMPLATE_PERMISSION).
import { useCallback, useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, IconPlus, Panel, Spinner, Tabs, toast, type DataColumn } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import { useClientOptions, useImportFields, useSetTemplateActive } from '../consignees/api'
import { ConsigneeTemplateEditorModal } from '../consignees/ConsigneeTemplateEditorModal'
import { templateSummary, type ImportTemplate } from '../consignees/consigneeRules'
import { ImportTemplateEditorModal } from '../consignees/ImportTemplateEditorModal'
import { useOrderImportFields, useSetOrderTemplateActive } from '../orders/importApi'
import { sortTemplates, TEMPLATE_PERMISSION, useAllTemplates, type TemplateEntity } from './templatesApi'

const ENTITIES = ['ORDER', 'CONSIGNEE'] as const

export default function TemplatesScreen() {
  const t = useT()
  const [entity, setEntity] = useState<TemplateEntity>('ORDER')
  const canChange = useCan(TEMPLATE_PERMISSION[entity])
  const list = useAllTemplates(entity, canChange)
  const orderFields = useOrderImportFields(canChange && entity === 'ORDER')
  const consigneeFields = useImportFields(canChange && entity === 'CONSIGNEE')
  const clients = useClientOptions(false)
  const setOrderActive = useSetOrderTemplateActive()
  const setConsigneeActive = useSetTemplateActive()
  const [editor, setEditor] = useState<{ template: ImportTemplate | null } | null>(null)

  const fieldLabel = useCallback(
    (field: string) => {
      const key = `${entity === 'ORDER' ? 'orders' : 'consignees'}.import.fields.${field}`
      const text = t(key)
      return text === key ? field : text
    },
    [t, entity],
  )
  const rows = useMemo(() => sortTemplates(list.data ?? []), [list.data])
  const clientList = useMemo(() => (clients.data ?? []).map((c) => ({ value: c.publicId ?? '', label: c.name ?? c.code ?? '' })), [clients.data])

  const setActive = async (tpl: ImportTemplate, active: boolean) => {
    try {
      await (entity === 'ORDER' ? setOrderActive : setConsigneeActive).mutateAsync({ publicId: tpl.publicId!, active })
      toast.success(t(active ? 'templates.reactivated' : 'templates.deactivated', { name: tpl.name ?? '' }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<ImportTemplate>[]>(
    () => [
      { id: 'name', header: t('templates.col.name'), cell: (x) => <b>{x.name}</b>, sortValue: (x) => x.name ?? '', card: 'title' },
      { id: 'client', header: t('templates.col.client'), cell: (x) => x.clientName || t('templates.allClients'), sortValue: (x) => x.clientName ?? '' },
      { id: 'mapping', header: t('templates.col.mapping'), cell: (x) => <span className="help">{templateSummary(x, fieldLabel) || '—'}</span>, sortValue: (x) => (x.columns ?? []).length },
      { id: 'columns', header: t('templates.col.columns'), cell: (x) => (x.columns ?? []).length, sortValue: (x) => (x.columns ?? []).length, align: 'end' },
      {
        id: 'state',
        header: t('templates.col.state'),
        cell: (x) => <Chip tone={x.isActive === false ? 'warn' : 'deliv'}>{t(x.isActive === false ? 'templates.inactive' : 'templates.active')}</Chip>,
        sortValue: (x) => (x.isActive === false ? 0 : 1),
      },
    ],
    [t, fieldLabel],
  )

  const fields = entity === 'ORDER' ? (orderFields.data ?? []) : (consigneeFields.data ?? [])

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('templates.title')}</h1>
          <p>{t('templates.subtitle')}</p>
        </div>
        {canChange && (
          <button type="button" className="btn flow" onClick={() => setEditor({ template: null })} disabled={fields.length === 0}>
            <IconPlus /> {t('templates.new')}
          </button>
        )}
      </div>
      <Tabs<TemplateEntity> tabs={ENTITIES.map((e) => ({ key: e, label: t(`templates.entities.${e}`) }))} value={entity} onChange={setEntity} label={t('templates.entity')} />
      <Panel flush icon={<IconTag />} title={t(`templates.entities.${entity}`)} badge={canChange && !list.isPending ? rows.length : undefined}>
        <div className="pb">
          {!canChange ? (
            <p className="help">{t('templates.noPermission', { perm: TEMPLATE_PERMISSION[entity] })}</p>
          ) : list.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t(`templates.entities.${entity}`)}
              columns={columns}
              rows={rows}
              rowKey={(x) => x.publicId ?? String(x.id)}
              defaultSort={{ id: 'name', desc: false }}
              empty={t('templates.empty')}
              rowActions={[
                { key: 'edit', label: t('templates.edit'), onClick: (x) => setEditor({ template: x }), visible: (x) => x.isActive !== false },
                { key: 'deactivate', label: t('templates.deactivate'), tone: 'danger', onClick: (x) => void setActive(x, false), visible: (x) => x.isActive !== false },
                { key: 'reactivate', label: t('templates.reactivate'), onClick: (x) => void setActive(x, true), visible: (x) => x.isActive === false },
              ]}
            />
          )}
        </div>
      </Panel>
      {editor &&
        (entity === 'ORDER' ? (
          <ImportTemplateEditorModal kind="ORDER" template={editor.template} fields={fields} fieldLabel={fieldLabel} onClose={() => setEditor(null)} onSaved={() => setEditor(null)} />
        ) : (
          <ConsigneeTemplateEditorModal template={editor.template} fields={fields} fieldLabel={fieldLabel} clients={clientList} onClose={() => setEditor(null)} onSaved={() => setEditor(null)} />
        ))}
    </div>
  )
}
