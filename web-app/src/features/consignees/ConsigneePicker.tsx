// Selector de consignatario para capturar una orden (Lote F-C1): combobox con buscador sobre los consignatarios activos del cliente
// MÁS los compartidos (`GET /locations?clientId&includeShared=true&locationType=DELIVERY`), que muestra «dirección · pueblo» y marca los
// que admiten número de factura repetido (`allowDupInvoice`). El botón «+» abre el MISMO modal de alta del catálogo
// (`ConsigneeFormModal`, con teléfono; exige `locations.create`) y deja elegido el recién creado. Dentro de un `<Field>`: `ConsigneePickerInput`.
import { useMemo, useState } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { ComboSelect, IconPlus, type ComboOption } from '../../kernel/ui'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { useClientConsignees, useSelectedConsignee } from './api'
import { ConsigneeFormModal } from './ConsigneeFormModal'
import { consigneeAddress, consigneeOptionLabel } from './consigneeRules'

export interface ConsigneePickerProps {
  /** Cliente de la orden (publicId); sin él el selector espera ("Elija primero un cliente"). */
  clientPublicId: string | null
  /** publicId del consignatario elegido ('' = ninguno). */
  value: string
  onChange: (publicId: string) => void
  id?: string
  invalid?: boolean
  required?: boolean
  'aria-describedby'?: string
  /** Etiqueta accesible cuando no hay un <label htmlFor> (la rejilla de la entrada rápida). */
  'aria-label'?: string
  /** false = sin la ✕ para quitar el consignatario (una parada menos al recorrer con Tab la rejilla de la entrada rápida). Por defecto true. */
  clearable?: boolean
  onBlur?: () => void
}

export function ConsigneePicker({ clientPublicId, value, onChange, id, invalid, required, onBlur, clearable, ...aria }: ConsigneePickerProps) {
  const t = useT()
  const canCreate = useCan('locations.create')
  const [creating, setCreating] = useState(false)
  const list = useClientConsignees(clientPublicId)
  const selected = useSelectedConsignee(clientPublicId, value)

  const options = useMemo<ComboOption[]>(() => {
    const rows = [...(list.data ?? [])]
    // el elegido aunque todavía no esté en la lista (recién creado)
    if (selected?.publicId && !rows.some((r) => r.publicId === selected.publicId)) rows.push(selected)
    return rows
      .filter((r) => r.isActive !== false && r.publicId)
      .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '', undefined, { sensitivity: 'base', numeric: true }))
      .map((r) => ({
        value: r.publicId ?? '',
        label: consigneeOptionLabel(r),
        hint: [consigneeAddress(r), r.allowDupInvoice ? t('orders.consignee.dupAllowedShort') : '', r.isShared ? t('consignees.shared') : '']
          .filter(Boolean)
          .join(' · '),
      }))
  }, [list.data, selected, t])

  const noList = list.isError
  return (
    // el modal de alta es un portal DENTRO del árbol de React del formulario de la orden: su `submit` subiría hasta el <form> de la orden
    // y la guardaría; se corta aquí
    <div className="oe-consrow" onSubmit={(e) => e.stopPropagation()}>
      <div className="oe-consbox">
        <ComboSelect
          id={id}
          options={options}
          value={value}
          clearable={clearable}
          onChange={(v) => onChange(v)}
          loading={list.isLoading && !!clientPublicId}
          disabled={!clientPublicId}
          invalid={invalid}
          required={required}
          onBlur={onBlur}
          placeholder={clientPublicId ? (noList ? t('orders.consignee.listDenied') : t('orders.consignee.search')) : t('orders.consignee.pickClientFirst')}
          {...aria}
        />
      </div>
      {canCreate && (
        <button
          type="button"
          className="btn sm oe-consadd"
          onClick={() => setCreating(true)}
          disabled={!clientPublicId}
          aria-label={t('orders.consignee.add')}
          title={clientPublicId ? t('orders.consignee.add') : t('orders.consignee.pickClientFirst')}
        >
          <IconPlus />
        </button>
      )}
      {canCreate && (
        <ConsigneeFormModal
          open={creating}
          consignee={null}
          phone={undefined}
          defaultOwner={clientPublicId ?? ''}
          onClose={() => setCreating(false)}
          onSaved={(publicId) => onChange(publicId)}
        />
      )}
    </div>
  )
}

/** `ConsigneePicker` dentro de un `<Field name="…">`: el valor del formulario es el publicId del consignatario ('' = ninguno). */
export function ConsigneePickerInput({ clientPublicId, onPicked }: { clientPublicId: string | null; onPicked?: (publicId: string) => void }) {
  const info = useFieldInfo('ConsigneePickerInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <ConsigneePicker
      clientPublicId={clientPublicId}
      id={info.id}
      value={(field.value as string | null | undefined) ?? ''}
      onChange={(v) => {
        field.onChange(v)
        onPicked?.(v)
      }}
      onBlur={field.onBlur}
      invalid={info.invalid}
      required={info.required}
      aria-describedby={info.describedBy}
    />
  )
}
