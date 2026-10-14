// Panel «Campos personalizados» del cliente (CLIENT): valores por PUT /custom-fields/values/CLIENT/{id}. No se pinta si el
// módulo CUSTOM_FIELDS está apagado o la compañía no definió campos para clientes. Escribir exige clients.update.
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { CustomFieldsForm, useCustomFieldDefinitions, useCustomFieldValues, useSaveCustomFields } from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { Form, Panel, toast } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import { CLIENT_ENTITY_TYPE, type ClientDetail } from './clientRules'

interface CustomValues {
  customFields: Record<string, unknown>
}

export function ClientCustomFieldsPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const canEdit = useCan('clients.update')
  const definitions = useCustomFieldDefinitions(CLIENT_ENTITY_TYPE)
  const { save } = useSaveCustomFields(CLIENT_ENTITY_TYPE)
  const form = useForm<CustomValues>({ defaultValues: { customFields: {} } })
  const values = useCustomFieldValues(CLIENT_ENTITY_TYPE, client.id)

  // cerrado: «campo: valor · campo: valor · +N» con los que tienen valor (hasta tres); sin valores, cuántos campos hay
  const defs = definitions.data ?? []
  const filled = (Array.isArray(values.data) ? values.data : []).filter((v) => v.displayValue != null && v.displayValue !== '')
  const labelOf = (key: string | null | undefined) => defs.find((d) => d.fieldKey === key)?.label ?? key ?? ''
  const summary =
    filled.length === 0
      ? t('clients.summary.customNone', { n: defs.length })
      : [...filled.slice(0, 3).map((v) => `${labelOf(v.fieldKey)}: ${v.displayValue}`), filled.length > 3 ? t('clients.summary.more', { n: filled.length - 3 }) : null]
          .filter(Boolean)
          .join(' · ')

  // sin módulo o sin definiciones no hay nada que mostrar (la consulta queda deshabilitada con el módulo apagado)
  if (!definitions.data || definitions.data.length === 0) return null

  return (
    <Panel
      icon={<IconTag />}
      title={t('clients.custom.title')}
      collapsible
      defaultCollapsed
      summary={summary}
    >
      <Form
        form={form}
        onSubmit={async () => {
          const problem = await save(client.id, form)
          // los errores por campo quedan bajo cada campo; el título va al aviso
          if (problem) {
            toast.error(problem.title)
            return
          }
          toast.success(t('clients.saved'))
        }}
      >
        <CustomFieldsForm entityType={CLIENT_ENTITY_TYPE} entityId={client.id} form={form} disabled={!canEdit} />
        <Can perm="clients.update">
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
            </button>
          </div>
        </Can>
      </Form>
    </Panel>
  )
}
