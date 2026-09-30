// Lote 1 (cambios de Almacén) — modal de alta/edición de una zona (`warehouse.manage`): POST /warehouses/{publicId}/zones
// y PATCH .../zones/{zoneId}. Lo comparten la lista de almacenes (bloque "Zonas de este almacén", maqueta `almacenes()`)
// y la pestaña Zonas de la ficha. El Tipo es un combobox con buscador sobre el catálogo `ZoneType` y el Código se edita
// también en edición (obligatorio y único en el almacén): el 409 del servidor ('Ya existe una zona con ese código en el
// almacén.') se muestra bajo el campo Código, no arriba del formulario.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, Form, Modal, TextInput, toast } from '../../kernel/ui'
import { useSaveWarehouseZone, type WarehouseZoneDto } from './api'

/** Formato del código de zona (WarehouseRules.NormalizeCode: letras, números, guion y guion bajo; máx. 30). */
const CODE_PATTERN = /^[A-Za-z0-9_-]+$/

export interface ZoneModalProps {
  /** publicId del almacén de la zona. */
  publicId: string
  /** null = alta; con valor = edición de esa zona. */
  zone: WarehouseZoneDto | null
  open: boolean
  onClose: () => void
}

export function ZoneModal({ publicId, zone, open, onClose }: ZoneModalProps) {
  const t = useT()
  const save = useSaveWarehouseZone()
  const { data: zoneTypes = [], isLoading: typesLoading } = useLookups('ZoneType')
  const typeOptions = useMemo(() => zoneTypes.map((z2) => ({ value: z2.code, label: z2.label })), [zoneTypes])
  const isEdit = zone !== null

  const schema = useMemo(
    () =>
      z.object({
        code: z
          .string()
          .trim()
          .min(1, t('warehouse.zones.errors.codeRequired'))
          .max(30, t('warehouse.zones.errors.codePattern'))
          .regex(CODE_PATTERN, t('warehouse.zones.errors.codePattern')),
        name: z.string().trim().min(1, t('warehouse.zones.errors.nameRequired')),
        zoneType: z.string(),
      }),
    [t],
  )
  const values = useMemo(() => ({ code: zone?.code ?? '', name: zone?.name ?? '', zoneType: zone?.zoneTypeCode ?? '' }), [zone])
  const form = useForm({ resolver: zodResolver(schema), values })
  const formId = 'warehouse-zone-save'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEdit ? t('warehouse.zones.editTitle') : t('warehouse.zones.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          try {
            if (isEdit && zone) {
              // PATCH: '' en zoneType quita el tipo (null = no cambiar)
              await save.mutateAsync({ publicId, action: 'update', zoneId: zone.id ?? 0, body: { code: v.code, name: v.name, zoneType: v.zoneType } })
              toast.success(t('warehouse.zones.saved'))
            } else {
              await save.mutateAsync({ publicId, action: 'create', body: { code: v.code, name: v.name, zoneType: v.zoneType || null } })
              toast.success(t('warehouse.zones.created'))
            }
          } catch (err) {
            // código repetido en el almacén: el mensaje del servidor va bajo el campo Código
            if (err instanceof ApiError && err.status === 409) {
              form.setError('code', { type: 'server', message: err.title })
              return
            }
            throw err
          }
          close()
        }}
      >
        <div className="r2">
          <Field name="code" label={t('warehouse.zones.code')} required help={isEdit ? t('warehouse.zones.codeEditHelp') : undefined}>
            <TextInput maxLength={30} autoCapitalize="characters" />
          </Field>
          <Field name="name" label={t('warehouse.zones.name')} required>
            <TextInput />
          </Field>
        </div>
        <Field name="zoneType" label={t('warehouse.zones.type')}>
          <ComboSelectInput options={typeOptions} loading={typesLoading} placeholder={t('warehouse.zones.typePlaceholder')} />
        </Field>
      </Form>
    </Modal>
  )
}
