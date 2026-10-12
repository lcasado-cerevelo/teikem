// Ficha del consignatario (derecha de la pantalla, maqueta «Ficha del consignatario»): Editar, número, nombre, dirección,
// pueblo, teléfono, cliente dueño, estado (interruptor con confirmación), «Número de factura repetido» (interruptor que guarda
// al instante) y, debajo, los campos personalizados (LOCATION). Las opciones de entrega van en una sección plegada.
import { useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { CustomFieldsForm, useCustomFieldDefinitions, useSaveCustomFields } from '../../kernel/custom-fields'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, EmptyState, Form, IconPin, IconTag, Panel, Spinner, toast } from '../../kernel/ui'
import { IconEdit } from '../../kernel/ui/actionIcons'
import { consigneeKeys, useConsignee, useConsigneeContacts, useSetConsigneeActive, useUpdateConsignee } from './api'
import { ConsigneeFormModal } from './ConsigneeFormModal'
import { LOCATION_ENTITY_TYPE, ownerLabel, pickPhone, timeInput, type Consignee } from './consigneeRules'

function Fact({ label, children, wide }: { label: string; children: ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? 'f cs-fact wide' : 'f cs-fact'}>
      <span className="cs-lbl">{label}</span>
      <div>{children}</div>
    </div>
  )
}

function CustomFieldsPanel({ consignee }: { consignee: Consignee }) {
  const t = useT()
  const canEdit = useCan('locations.update')
  const definitions = useCustomFieldDefinitions(LOCATION_ENTITY_TYPE)
  const { save } = useSaveCustomFields(LOCATION_ENTITY_TYPE)
  const form = useForm<{ customFields: Record<string, unknown> }>({ defaultValues: { customFields: {} } })

  // sin módulo o sin definiciones no hay nada que mostrar
  if (!definitions.data || definitions.data.length === 0 || consignee.id == null) return null
  return (
    <Panel icon={<IconTag />} title={t('consignees.custom.title')}>
      <Form
        form={form}
        onSubmit={async () => {
          const problem = await save(consignee.id ?? 0, form)
          if (problem) {
            toast.error(problem.title)
            return
          }
          toast.success(t('consignees.saved'))
        }}
      >
        <CustomFieldsForm entityType={LOCATION_ENTITY_TYPE} entityId={consignee.id} form={form} disabled={!canEdit} />
        <Can perm="locations.update">
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

export function ConsigneeDetailPanel({ publicId, defaultOwner }: { publicId: string; defaultOwner: string }) {
  const t = useT()
  const f = useFormat()
  const qc = useQueryClient()
  const canUpdate = useCan('locations.update')
  const { data: c, isPending, error } = useConsignee(publicId)
  const contacts = useConsigneeContacts(c?.id ?? null)
  const { data: countries = [] } = useLookups('Country')
  const setActive = useSetConsigneeActive(publicId)
  const update = useUpdateConsignee(publicId)
  const [editing, setEditing] = useState(false)
  const [confirm, setConfirm] = useState<'deactivate' | 'reactivate' | null>(null)

  if (isPending) {
    return (
      <Panel>
        <Spinner block />
      </Panel>
    )
  }
  if (error || !c) {
    return (
      <Panel>
        <EmptyState icon={<IconPin />} title={error ? applyProblemDetails(error).title : t('consignees.notFound')} />
      </Panel>
    )
  }

  const phone = pickPhone(contacts.data)
  const countryLabel = c.countryLabel ?? countries.find((o) => o.code === c.country)?.label ?? c.country ?? ''
  const window = c.defaultWindowStart && c.defaultWindowEnd ? `${f.timeOfDay(timeInput(c.defaultWindowStart))} – ${f.timeOfDay(timeInput(c.defaultWindowEnd))}` : '—'
  const dup = c.allowDupInvoice === true

  const toggleDup = async (next: boolean) => {
    try {
      await update.mutateAsync({ allowDupInvoice: next, rowVersion: c.rowVersion ?? null })
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
      // 409: otro usuario guardó antes; se relee la ficha
      void qc.invalidateQueries({ queryKey: consigneeKeys.detail(publicId) })
    }
  }

  return (
    <div className="cs-stack">
      <Panel
        icon={<IconPin />}
        title={t('consignees.info')}
        actions={
          <Can perm="locations.update">
            <button type="button" className="btn sm" onClick={() => setEditing(true)}>
              <IconEdit /> {t('consignees.edit')}
            </button>
          </Can>
        }
      >
        <div className="cs-grid">
          <Fact label={t('consignees.number')}>{c.code || '—'}</Fact>
          <Fact label={t('consignees.name')}>{c.name}</Fact>
          <Fact label={t('consignees.address')} wide>
            {c.line1 || '—'}
          </Fact>
          <Fact label={t('consignees.town')}>{c.city || '—'}</Fact>
          <Fact label={t('consignees.phone')}>{phone?.value ? f.phone(phone.value) : '—'}</Fact>
          <Fact label={t('consignees.scope')} wide>
            {ownerLabel(c, t('consignees.shared')) || '—'}
          </Fact>
          <Fact label={t('consignees.status')} wide>
            <label className="sw">
              <input
                type="checkbox"
                role="switch"
                checked={c.isActive === true}
                disabled={!canUpdate}
                aria-label={t('consignees.status')}
                onChange={() => setConfirm(c.isActive ? 'deactivate' : 'reactivate')}
              />
              <span className="tk" aria-hidden="true" />
              <span>{c.isActive ? t('consignees.active') : t('consignees.inactive')}</span>
            </label>
          </Fact>
          <Fact label={t('consignees.dupInvTitle')} wide>
            <label className="sw">
              <input type="checkbox" role="switch" checked={dup} disabled={!canUpdate || update.isPending} aria-label={t('consignees.dupInvTitle')} onChange={(e) => void toggleDup(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{dup ? t('consignees.dupInvOn') : t('consignees.dupInvOff')}</span>
            </label>
            <p className="help">{t('consignees.dupInvHint')}</p>
          </Fact>
        </div>

        <details className="cs-more">
          <summary>{t('consignees.delivery.title')}</summary>
          <div className="cs-grid">
            <Fact label={t('consignees.delivery.line2')} wide>
              {c.line2 || '—'}
            </Fact>
            <Fact label={t('consignees.delivery.state')}>{c.state || '—'}</Fact>
            <Fact label={t('consignees.delivery.zip')}>{c.postalCode || '—'}</Fact>
            <Fact label={t('consignees.delivery.country')}>{countryLabel || '—'}</Fact>
            <Fact label={t('consignees.delivery.window')}>{window}</Fact>
            <Fact label={t('consignees.delivery.serviceMinutes')}>{c.defaultServiceMinutes ?? 0}</Fact>
            <Fact label={t('consignees.delivery.accessNotes')} wide>
              {c.accessNotes || '—'}
            </Fact>
            <Fact label={t('consignees.delivery.deliveryNotes')} wide>
              {c.deliveryNotes || '—'}
            </Fact>
          </div>
        </details>
      </Panel>

      <CustomFieldsPanel consignee={c} />

      <ConsigneeFormModal
        key={`${c.publicId}-${editing ? 'edit' : 'closed'}`}
        open={editing}
        consignee={c}
        phone={phone}
        defaultOwner={defaultOwner}
        onClose={() => setEditing(false)}
        onSaved={() => undefined}
      />
      <ConfirmDialog
        open={confirm !== null}
        tone={confirm === 'deactivate' ? 'danger' : 'flow'}
        title={confirm === 'deactivate' ? t('consignees.deactivateTitle') : t('consignees.reactivateTitle')}
        message={t(confirm === 'deactivate' ? 'consignees.deactivateBody' : 'consignees.reactivateBody', { name: c.name ?? '' })}
        confirmLabel={confirm === 'deactivate' ? t('consignees.deactivate') : t('consignees.reactivate')}
        onConfirm={async () => {
          const active = confirm === 'reactivate'
          await setActive.mutateAsync(active)
          toast.success(t(active ? 'consignees.reactivated' : 'consignees.deactivated'))
        }}
        onClose={() => setConfirm(null)}
      />
    </div>
  )
}
