// Lote 14 (P8) — "Nuevo conteo" (selección manual), el modal que antes vivía en la lista: almacén, zonas y posiciones
// (`POST /cycle-counts`, warehouse.count; sin zonas ni posiciones toma todo el saldo en mano del almacén, máx. 1000 líneas).
// El selector de posiciones lee todas las páginas del listado paginado (Lote 1) con `fetchAllPages`. Al crear, avisa a la
// pantalla (`onCreated`), que refresca la lista y elige el conteo nuevo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useController, useForm, useFormContext, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, SearchMultiSelect, toast } from '../../kernel/ui'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { useCreateCycleCount, useWarehouseZones, warehouseKeys, type CycleCountDetailDto } from './api'
import { WarehousePickerInput } from './pickers'

/** SearchMultiSelect dentro de un <Field name="…">: el valor del formulario es un arreglo de strings. */
export function MultiSelectInput({ options, placeholder, disabled }: { options: readonly { value: string; label: string }[]; placeholder?: string; disabled?: boolean }) {
  const info = useFieldInfo('MultiSelectInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <SearchMultiSelect
      id={info.id}
      options={options}
      value={(field.value as string[] | undefined) ?? []}
      onChange={(v) => field.onChange(v)}
      onBlur={field.onBlur}
      placeholder={placeholder}
      disabled={disabled}
      invalid={info.invalid}
      describedBy={info.describedBy}
      buttonRef={field.ref}
    />
  )
}

export function CreateCountModal({ onClose, onCreated }: { onClose: () => void; onCreated?: (created: CycleCountDetailDto) => void }) {
  const t = useT()
  const create = useCreateCycleCount()
  const schema = useMemo(
    () =>
      z
        .object({ warehousePublicId: z.string().nullable(), zoneIds: z.array(z.string()), binIds: z.array(z.string()) })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
        }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { warehousePublicId: null as string | null, zoneIds: [] as string[], binIds: [] as string[] } })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const zoneIds = useWatch({ control: form.control, name: 'zoneIds' })
  const zones = useWarehouseZones(warehousePublicId, {}, { handleAccessDenied: false })
  // Todas las posiciones activas del almacén (o de las zonas elegidas, filtro del servidor) en páginas de 200, hasta 10 000;
  // si se corta se avisa bajo el campo. Misma raíz de clave que useWarehouseBins: se invalida con las posiciones.
  const binsQuery = useMemo(() => ({ includeInactive: false, zoneIds: zoneIds.length > 0 ? zoneIds.map(Number) : undefined }), [zoneIds])
  const bins = useQuery({
    queryKey: [warehouseKeys.bins[0], { publicId: warehousePublicId, ...binsQuery, all: true }],
    queryFn: () =>
      fetchAllPages((skip, take) =>
        unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId: warehousePublicId ?? '' }, query: { ...binsQuery, skip, take } } })),
      ),
    enabled: Boolean(warehousePublicId),
    meta: { handleAccessDenied: false },
  })
  const zoneOptions = useMemo(
    () => (zones.data ?? []).filter((zone) => zone.isActive !== false).map((zone) => ({ value: String(zone.id), label: [zone.code, zone.name].filter(Boolean).join(' · ') })),
    [zones.data],
  )
  const binOptions = useMemo(
    () =>
      (bins.data?.items ?? [])
        .filter((b) => b.isActive !== false && (zoneIds.length === 0 || zoneIds.includes(String(b.zoneId))))
        .map((b) => ({ value: String(b.id), label: [b.code, b.zoneCode].filter(Boolean).join(' · ') })),
    [bins.data, zoneIds],
  )
  const formId = 'cycle-count-create'

  return (
    <Modal
      open
      title={t('warehouse.cycleCounts.new')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('warehouse.cycleCounts.create')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const created = await create.mutateAsync({
            warehousePublicId: v.warehousePublicId,
            zoneIds: v.zoneIds.length > 0 ? v.zoneIds.map(Number) : null,
            binIds: v.binIds.length > 0 ? v.binIds.map(Number) : null,
          })
          toast.success(t('warehouse.cycleCounts.created', { number: created.count?.number ?? '', count: created.lines?.length ?? 0 }))
          onCreated?.(created)
          onClose()
        }}
      >
        <Field name="warehousePublicId" label={t('warehouse.cycleCounts.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        <div className="r2">
          <Field name="zoneIds" label={t('warehouse.cycleCounts.fields.zones')}>
            <MultiSelectInput options={zoneOptions} placeholder={t('warehouse.cycleCounts.fields.allZones')} disabled={!warehousePublicId} />
          </Field>
          <Field name="binIds" label={t('warehouse.cycleCounts.fields.bins')}>
            <MultiSelectInput options={binOptions} placeholder={t('warehouse.cycleCounts.fields.allBins')} disabled={!warehousePublicId} />
          </Field>
        </div>
        {bins.data?.truncated && <p className="note">{t('warehouse.cycleCounts.fields.binsTruncated', { count: bins.data.items.length })}</p>}
        <p className="note">{t('warehouse.cycleCounts.createHelp')}</p>
      </Form>
    </Modal>
  )
}
