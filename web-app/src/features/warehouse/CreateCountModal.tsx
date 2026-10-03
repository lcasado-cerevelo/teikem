// Lote 14 (P8) — "Nuevo conteo" (selección manual), el modal que antes vivía en la lista: almacén, zonas y posiciones
// (`POST /cycle-counts`, warehouse.count; sin zonas ni posiciones toma todo el saldo en mano del almacén, máx. 1000 líneas).
// Lote F13 (decisión del dueño 2026-10-03): dos opciones con pestañas, "Por posiciones" (lo de siempre) y "Por producto": almacén +
// un producto (`productPublicIds: [producto]`, sin posiciones ni `allowEmpty`); el servidor arma una línea por posición/lote con
// existencia (origen PRODUCT). Si el producto no tiene existencia el servidor responde 400 en `filters` y el mensaje se muestra
// bajo el selector de producto.
// Segundo bloque de decisiones (2026-10-03): junto al selector, el switch "Solo con existencia" (activado de entrada, sin guardar la
// preferencia) limita la lista a los productos con existencia en mano en el almacén elegido (`onlyOnHand` + `warehousePublicId` de
// GET /products: es la misma existencia que cuenta el servidor); apagado se ven todos y el 400 sigue saliendo bajo el selector.
// El selector de posiciones lee todas las páginas del listado paginado (Lote 1) con `fetchAllPages`. Al crear, avisa a la
// pantalla (`onCreated`), que refresca la lista y elige el conteo nuevo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useController, useForm, useFormContext, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, SearchMultiSelect, Tabs, toast } from '../../kernel/ui'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { useCreateCycleCount, useWarehouseZones, warehouseKeys, type CycleCountDetailDto } from './api'
import { remapProblemFields } from './lineRules'
import { ProductPickerInput, WarehousePickerInput } from './pickers'

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

type CreateMode = 'bins' | 'product'

export function CreateCountModal({ onClose, onCreated }: { onClose: () => void; onCreated?: (created: CycleCountDetailDto) => void }) {
  const t = useT()
  const create = useCreateCycleCount()
  const schema = useMemo(
    () =>
      z
        .object({
          mode: z.enum(['bins', 'product']),
          warehousePublicId: z.string().nullable(),
          zoneIds: z.array(z.string()),
          binIds: z.array(z.string()),
          productPublicId: z.string().nullable(),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (v.mode === 'product' && !v.productPublicId) ctx.addIssue({ code: 'custom', path: ['productPublicId'], message: t('warehouse.cycleCounts.errors.productRequired') })
        }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { mode: 'bins' as CreateMode, warehousePublicId: null as string | null, zoneIds: [] as string[], binIds: [] as string[], productPublicId: null as string | null },
  })
  const mode = useWatch({ control: form.control, name: 'mode' })
  const [onlyWithStock, setOnlyWithStock] = useState(true)
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const zoneIds = useWatch({ control: form.control, name: 'zoneIds' })
  const byBins = mode === 'bins'
  const zones = useWarehouseZones(byBins ? warehousePublicId : null, {}, { handleAccessDenied: false })
  // Todas las posiciones activas del almacén (o de las zonas elegidas, filtro del servidor) en páginas de 200, hasta 10 000;
  // si se corta se avisa bajo el campo. Misma raíz de clave que useWarehouseBins: se invalida con las posiciones.
  const binsQuery = useMemo(() => ({ includeInactive: false, zoneIds: zoneIds.length > 0 ? zoneIds.map(Number) : undefined }), [zoneIds])
  const bins = useQuery({
    queryKey: [warehouseKeys.bins[0], { publicId: warehousePublicId, ...binsQuery, all: true }],
    queryFn: () =>
      fetchAllPages((skip, take) =>
        unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId: warehousePublicId ?? '' }, query: { ...binsQuery, skip, take } } })),
      ),
    enabled: Boolean(warehousePublicId) && byBins,
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
          const body =
            v.mode === 'product'
              ? { warehousePublicId: v.warehousePublicId, productPublicIds: v.productPublicId ? [v.productPublicId] : null }
              : {
                  warehousePublicId: v.warehousePublicId,
                  zoneIds: v.zoneIds.length > 0 ? v.zoneIds.map(Number) : null,
                  binIds: v.binIds.length > 0 ? v.binIds.map(Number) : null,
                }
          // El 400 del servidor viene en `filters` ("no seleccionan inventario…"): por producto se pinta bajo el selector.
          const created = await create
            .mutateAsync(body)
            .catch((err: unknown) => {
              throw v.mode === 'product' ? remapProblemFields(err, (f) => (f === 'filters' ? 'productPublicId' : null)) : err
            })
          toast.success(t('warehouse.cycleCounts.created', { number: created.count?.number ?? '', count: created.lines?.length ?? 0 }))
          onCreated?.(created)
          onClose()
        }}
      >
        <div className="cc-create-tabs">
          <Tabs<CreateMode>
            label={t('warehouse.cycleCounts.new')}
            value={mode}
            onChange={(m) => {
              form.setValue('mode', m)
              form.clearErrors()
            }}
            tabs={[
              { key: 'bins', label: t('warehouse.cycleCounts.modes.bins') },
              { key: 'product', label: t('warehouse.cycleCounts.modes.product') },
            ]}
          />
        </div>
        <Field name="warehousePublicId" label={t('warehouse.cycleCounts.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        {byBins ? (
          <>
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
          </>
        ) : (
          <>
            <Field name="productPublicId" label={t('warehouse.cycleCounts.fields.product')} required>
              <ProductPickerInput warehousePublicId={onlyWithStock ? warehousePublicId : undefined} onlyOnHand={onlyWithStock} />
            </Field>
            <div className="f">
              <label className="sw cc-sw">
                <input type="checkbox" role="switch" checked={onlyWithStock} onChange={(e) => setOnlyWithStock(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('warehouse.cycleCounts.fields.onlyWithStock')}</span>
              </label>
              <p className="help">{t('warehouse.cycleCounts.fields.onlyWithStockHelp')}</p>
            </div>
            <p className="note">{t('warehouse.cycleCounts.createProductHelp')}</p>
          </>
        )}
      </Form>
    </Modal>
  )
}
