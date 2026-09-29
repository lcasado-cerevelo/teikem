// Modal de alta/edición de una posición (`warehouse.manage`): POST /warehouses/{publicId}/bins y PATCH .../bins/{binId}.
// Lo comparten la ficha del almacén (pestaña Posiciones) y Ubicaciones (botón "Nueva posición", maqueta `openBinModal()`).
// `ReadOnlyField` es el campo inmutable (código, zona) que también usan los demás modales de la ficha del almacén.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Select, TextInput, toast } from '../../kernel/ui'
import { useSaveWarehouseBin, type WarehouseBinDto, type WarehouseZoneDto } from './api'

/** Texto disabled fuera del formulario, para un campo inmutable (code, zone). */
export function ReadOnlyField({ label, value, help }: { label: string; value: string; help?: string }) {
  return (
    <div className="f">
      <label>{label}</label>
      <input value={value} disabled readOnly />
      {help && <p className="help">{help}</p>}
    </div>
  )
}

const MAX_WEIGHT_DECIMALS = 1000

export interface BinModalProps {
  /** publicId del almacén dueño de la posición. */
  publicId: string
  /** Zonas activas del almacén (el alta elige una; en edición la zona no cambia). */
  zones: readonly WarehouseZoneDto[]
  /** null = alta; con valor = edición de esa posición. */
  bin: WarehouseBinDto | null
  open: boolean
  onClose: () => void
}

export function BinModal({ publicId, zones, bin, open, onClose }: BinModalProps) {
  const t = useT()
  const save = useSaveWarehouseBin()
  const isEdit = bin !== null

  const schema = useMemo(
    () =>
      z
        .object({
          zoneId: isEdit ? z.string() : z.string().min(1, t('warehouse.bins.errors.zoneRequired')),
          code: z.string().trim(),
          aisle: z.string().trim(),
          rack: z.string().trim(),
          level: z.string().trim(),
          position: z.string().trim(),
          maxWeightKg: z
            .number()
            .nullable()
            .refine((v) => v == null || v > 0, t('warehouse.bins.errors.maxWeightPositive'))
            .refine((v) => v == null || Number.isInteger(Math.round(v * MAX_WEIGHT_DECIMALS)), t('warehouse.bins.errors.maxWeightDecimals')),
        })
        .refine(
          (v) => isEdit || v.code.trim() !== '' || [v.aisle, v.rack, v.level, v.position].some((x) => x.trim() !== ''),
          { message: t('warehouse.bins.errors.codeOrLocation'), path: ['code'] },
        ),
    [t, isEdit],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      zoneId: bin?.zoneId != null ? String(bin.zoneId) : '',
      code: bin?.code ?? '',
      aisle: bin?.aisle ?? '',
      rack: bin?.rack ?? '',
      level: bin?.level ?? '',
      position: bin?.position ?? '',
      maxWeightKg: bin?.maxWeightKg ?? null,
    },
  })
  const formId = 'warehouse-bin-save'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEdit ? t('warehouse.bins.edit') : t('warehouse.bins.new')}
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
          if (isEdit && bin) {
            await save.mutateAsync({
              publicId,
              action: 'update',
              binId: bin.id ?? 0,
              body: {
                aisle: v.aisle,
                rack: v.rack,
                level: v.level,
                position: v.position,
                ...(v.maxWeightKg != null ? { maxWeightKg: v.maxWeightKg } : bin.maxWeightKg != null ? { clearMaxWeight: true } : {}),
              },
            })
            toast.success(t('warehouse.bins.saved'))
          } else {
            await save.mutateAsync({
              publicId,
              action: 'create',
              body: {
                zoneId: Number(v.zoneId),
                code: v.code || null,
                aisle: v.aisle || null,
                rack: v.rack || null,
                level: v.level || null,
                position: v.position || null,
                maxWeightKg: v.maxWeightKg,
              },
            })
            toast.success(t('warehouse.bins.created'))
          }
          close()
        }}
      >
        {isEdit ? (
          <div className="r2">
            <ReadOnlyField label={t('warehouse.bins.code')} value={bin?.code ?? ''} help={t('warehouse.bins.codeHelp')} />
            <ReadOnlyField label={t('warehouse.bins.zone')} value={bin?.zoneCode ?? ''} />
          </div>
        ) : (
          <div className="r2">
            <Field name="zoneId" label={t('warehouse.bins.zone')} required>
              <Select options={zones.map((z) => ({ value: String(z.id), label: z.code ?? '' }))} placeholder="" />
            </Field>
            <Field name="code" label={t('warehouse.bins.code')}>
              <TextInput />
            </Field>
          </div>
        )}
        <div className="r3">
          <Field name="aisle" label={t('warehouse.bins.aisle')}>
            <TextInput />
          </Field>
          <Field name="rack" label={t('warehouse.bins.rack')}>
            <TextInput />
          </Field>
          <Field name="level" label={t('warehouse.bins.level')}>
            <TextInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="position" label={t('warehouse.bins.position')}>
            <TextInput />
          </Field>
          <Field name="maxWeightKg" label={t('warehouse.bins.maxWeight')}>
            <NumberInput min={0} step="0.001" />
          </Field>
        </div>
      </Form>
    </Modal>
  )
}
