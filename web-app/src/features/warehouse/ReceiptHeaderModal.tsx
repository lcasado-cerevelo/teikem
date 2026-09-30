// Lote 13 — modal del ENCABEZADO del recibo: alta ("Nuevo recibo" y "Recibir" de un aviso), edición (doble clic en la
// lista o el lápiz del detalle) y borrado. Campos: Origen (Ciego / Devolución / Contra aviso de llegada / Contra orden de
// compra — esta solo con purchasing.receive y el módulo PURCHASING), Almacén, Aviso u Orden de compra (`ComboSelectInput`,
// solo en el alta), Posición de recepción (`BinPickerInput` STAGING/CROSSDOCK: queda como la del encabezado), Muelle
// (`ComboSelectInput` sobre los muelles del almacén), Transporte y Referencia (máximo 80).
// - Alta: `POST /receipts` sin líneas (ciego/devolución nace Esperado; con documento, Recibiendo con las líneas del
//   documento). Quien lo abre recibe la ficha (`onCreated`) para dejarlo primero y elegido en la lista.
// - Edición (`PATCH /receipts/{publicId}`, warehouse.receive): solo lo que cambió; tipo Ciego ↔ Devolución solo sin
//   documento; almacén solo sin documento y sin líneas (al cambiarlo se limpian posición y muelle); `rowVersion` de la
//   caché al enviar (cambia con cada línea guardada). Confirmado o sin permiso: solo lectura.
// - "Borrar recibo" (warehouse.receive, si `canDelete`: abierto y sin cruce de muelle) con confirmación.
// Errores del API bajo su campo (`type` → Origen); el título arriba del formulario. Manual 06 §4.
// Lote 16: "Modo de recepción" (Con acomodo / Directo a posición; D1): en el alta, por defecto el del almacén elegido (se
// manda solo si hay uno); en la edición, el del recibo, y solo se cambia mientras está abierto. En directo se oculta
// "Posición de recepción" (cada línea lleva su posición destino).
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { ModuleKeys, useCan, useModule } from '../../kernel/access'
import type { components } from '../../kernel/api/schema'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { ComboSelectInput, ConfirmDialog, Field, Form, Modal, Select, Spinner, TextInput, toast, type ComboOption } from '../../kernel/ui'
import {
  useAsns,
  useCreateReceipt,
  useDeleteReceipt,
  usePurchaseOrders,
  useReceipt,
  useUpdateReceiptHeader,
  useWarehouseDocks,
  useWarehouses,
  warehouseKeys,
  type ReceiptDetailDto,
} from './api'
import { ReadOnlyField } from './BinModal'
import { formatDate, remapProblemFields } from './lineRules'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import { hasDocument, receiptOrigin, receiptOriginText } from './receiptFilters'
import {
  isDirectMode,
  normalizeReceivingMode,
  RECEIVING_MODE_DOMAIN,
  RECEIVING_ZONE_TYPES,
  receivingModeChanged,
  receivingModeLabel,
} from './receivingMode'
import { useReceivingModeOptions } from './useReceivingModeOptions'

type Schemas = components['schemas']
type Source = 'BLIND' | 'RETURN' | 'ASN' | 'PO'

/** Tipos de zona donde se recibe (posición de recepción del encabezado). */
const RECEIVING_ZONES = RECEIVING_ZONE_TYPES
const TEXT_MAX = 80

export interface ReceiptHeaderModalProps {
  /** Recibo a editar; null = alta. */
  publicId: string | null
  /** Alta desde "Recibir" de un aviso: origen, almacén y aviso ya elegidos. */
  preset?: { asnId: number; warehousePublicId: string | null; warehouseCode?: string | null }
  onClose: () => void
  /** Alta: la ficha creada (queda en caché). */
  onCreated?: (receipt: ReceiptDetailDto) => void
  onDeleted?: (publicId: string) => void
}

export function ReceiptHeaderModal({ publicId, preset, onClose, onCreated, onDeleted }: ReceiptHeaderModalProps) {
  const t = useT()
  const { data: receipt, isLoading, error } = useReceipt(publicId)
  if (publicId && (isLoading || error || !receipt?.header)) {
    return (
      <Modal open title={t('warehouse.receipts.header.editTitle', { number: '' })} onClose={onClose}>
        {isLoading ? <Spinner block /> : <p className="ferr">{error?.message ?? t('errors.generic')}</p>}
      </Modal>
    )
  }
  return <HeaderForm receipt={publicId ? (receipt ?? null) : null} preset={preset} onClose={onClose} onCreated={onCreated} onDeleted={onDeleted} />
}

function HeaderForm({
  receipt,
  preset,
  onClose,
  onCreated,
  onDeleted,
}: {
  receipt: ReceiptDetailDto | null
  preset?: ReceiptHeaderModalProps['preset']
  onClose: () => void
  onCreated?: (receipt: ReceiptDetailDto) => void
  onDeleted?: (publicId: string) => void
}) {
  const t = useT()
  const lang = useLang()
  const qc = useQueryClient()
  const create = useCreateReceipt()
  const update = useUpdateReceiptHeader()
  const del = useDeleteReceipt()
  const canReceive = useCan('warehouse.receive')
  // Recibir contra PO: además purchasing.receive y el módulo PURCHASING; sin ambos la opción no se pinta.
  const hasPoPerm = useCan('purchasing.receive')
  const purchasingOn = useModule(ModuleKeys.Purchasing)
  const canReceivePo = hasPoPerm && purchasingOn
  const [deleting, setDeleting] = useState(false)

  const h = receipt?.header
  const editing = receipt !== null
  const doc = editing && hasDocument(h?.origin)
  const readOnly = editing && (!h?.isOpen || !canReceive)
  const initialSource: Source = editing ? receiptOrigin(h?.origin) : preset ? 'ASN' : 'BLIND'
  const sourceEditable = !editing || !doc
  const warehouseEditable = editing ? !doc && (h?.lineCount ?? 0) === 0 : !preset

  const schema = useMemo(
    () =>
      z
        .object({
          source: z.string(),
          warehousePublicId: z.string().nullable(),
          asnId: z.string(),
          purchaseOrderPublicId: z.string(),
          stagingBinId: z.string(),
          receivingMode: z.string(),
          dockId: z.string(),
          carrier: z.string().max(TEXT_MAX, t('warehouse.receipts.errors.carrierMax')),
          reference: z.string().max(TEXT_MAX, t('warehouse.receipts.errors.referenceMax')),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (editing) return
          if (v.source === 'ASN' && !v.asnId) ctx.addIssue({ code: 'custom', path: ['asnId'], message: t('warehouse.receipts.errors.asnRequired') })
          if (v.source === 'PO' && !v.purchaseOrderPublicId)
            ctx.addIssue({ code: 'custom', path: ['purchaseOrderPublicId'], message: t('warehouse.receipts.errors.poRequired') })
        }),
    [t, editing],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      source: initialSource as string,
      warehousePublicId: (editing ? h?.warehousePublicId : preset?.warehousePublicId) ?? null,
      asnId: preset ? String(preset.asnId) : '',
      purchaseOrderPublicId: '',
      stagingBinId: h?.defaultStagingBinId != null ? String(h.defaultStagingBinId) : '',
      // alta: '' = el del almacén (se pone al elegirlo)
      receivingMode: editing ? normalizeReceivingMode(h?.receivingModeCode) : '',
      dockId: h?.dockId != null ? String(h.dockId) : '',
      carrier: h?.carrier ?? '',
      reference: h?.reference ?? '',
    },
  })
  const source = useWatch({ control: form.control, name: 'source' }) as Source
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const modeValue = useWatch({ control: form.control, name: 'receivingMode' })

  // Lote 16: modo del almacén elegido (misma consulta que WarehousePicker: comparte caché)
  const { data: modeLookups = [] } = useLookups(RECEIVING_MODE_DOMAIN)
  const warehouses = useWarehouses({ includeInactive: false }, { enabled: !editing, handleAccessDenied: false })
  const warehouseMode = warehouses.data?.find((w) => w.publicId === warehousePublicId)?.receivingModeCode ?? null
  const modeOptions = useReceivingModeOptions()
  // alta: el modo sigue al del almacén mientras el usuario no lo haya elegido a mano
  useEffect(() => {
    if (editing || form.getFieldState('receivingMode').isDirty) return
    form.setValue('receivingMode', warehousePublicId && warehouseMode ? normalizeReceivingMode(warehouseMode) : '')
  }, [editing, form, warehousePublicId, warehouseMode])
  const direct = isDirectMode(modeValue || warehouseMode)

  // al cambiar de almacén el muelle y el documento ya no aplican (la posición la quita el propio BinPicker)
  const lastWarehouse = useRef(warehousePublicId)
  useEffect(() => {
    if (lastWarehouse.current === warehousePublicId) return
    lastWarehouse.current = warehousePublicId
    form.setValue('dockId', '')
    if (!preset) form.setValue('asnId', '')
    form.setValue('purchaseOrderPublicId', '')
  }, [warehousePublicId, form, preset])

  const asns = useAsns(
    { warehousePublicId: warehousePublicId ?? undefined, status: ['EXPECTED'] },
    { enabled: !editing && source === 'ASN' && Boolean(warehousePublicId) },
  )
  const pos = usePurchaseOrders(
    { warehousePublicId: warehousePublicId ?? undefined, status: ['SENT', 'PARTIAL'], take: 200 },
    { enabled: !editing && source === 'PO' && canReceivePo && Boolean(warehousePublicId), handleAccessDenied: false },
  )
  const docks = useWarehouseDocks(warehousePublicId, { includeInactive: false }, { enabled: !readOnly && Boolean(warehousePublicId), handleAccessDenied: false })

  const sourceOptions = useMemo(() => {
    const all: Source[] = editing ? ['BLIND', 'RETURN'] : ['BLIND', 'RETURN', 'ASN', ...(canReceivePo ? (['PO'] as const) : [])]
    return all.map((s) => ({ value: s, label: t(`warehouse.receipts.sources.${s}`) }))
  }, [t, editing, canReceivePo])
  const asnOptions = useMemo<ComboOption[]>(
    () =>
      (asns.data ?? [])
        .filter((a) => !a.receiptPublicId || a.id === preset?.asnId)
        .map((a) => ({
          value: String(a.id),
          label: [`#${a.id}`, a.reference, a.clientName ?? a.purchaseOrderNumber].filter(Boolean).join(' · '),
          hint: a.expectedDate ? formatDate(a.expectedDate, lang) : undefined,
        })),
    [asns.data, preset?.asnId, lang],
  )
  const poOptions = useMemo<ComboOption[]>(
    () => (pos.data?.items ?? []).map((p) => ({ value: p.publicId ?? '', label: [p.number, p.supplierName].filter(Boolean).join(' · ') })),
    [pos.data],
  )
  const currentDockId = h?.dockId ?? null
  const currentDockCode = h?.dockCode ?? null
  const currentWarehouse = h?.warehousePublicId ?? null
  const dockOptions = useMemo<ComboOption[]>(() => {
    const list: ComboOption[] = (docks.data ?? []).map((d) => ({ value: String(d.id), label: d.code ?? String(d.id), hint: d.dockType ?? undefined }))
    // el muelle actual dado de baja se conserva con su código
    if (currentDockId != null && currentWarehouse === warehousePublicId && !list.some((o) => o.value === String(currentDockId)))
      list.unshift({ value: String(currentDockId), label: currentDockCode ?? String(currentDockId) })
    return list
  }, [docks.data, currentDockId, currentDockCode, currentWarehouse, warehousePublicId])

  const formId = editing ? 'receipt-header-edit' : 'receipt-header-create'
  const number = h?.number ?? ''
  const title = editing ? t('warehouse.receipts.header.editTitle', { number }) : t('warehouse.receipts.header.newTitle')
  const canDelete = editing && canReceive && receipt?.canDelete === true
  const busy = form.formState.isSubmitting

  const deleteButton = canDelete ? (
    <button type="button" className="btn danger rcp-delete" onClick={() => setDeleting(true)} disabled={busy}>
      {t('warehouse.receipts.header.delete')}
    </button>
  ) : null

  const confirmDelete = (
    <ConfirmDialog
      open={deleting}
      tone="danger"
      title={t('warehouse.receipts.header.deleteTitle')}
      message={t('warehouse.receipts.header.deleteBody', { number })}
      confirmLabel={t('warehouse.receipts.header.delete')}
      onConfirm={async () => {
        const id = h?.publicId ?? ''
        await del.mutateAsync(id)
        toast.success(t('warehouse.receipts.header.deleted', { number }))
        onDeleted?.(id)
        onClose()
      }}
      onClose={() => setDeleting(false)}
    />
  )

  if (readOnly && h) {
    const typeLabel = t(`warehouse.receipts.sources.${receiptOrigin(h.origin)}`)
    return (
      <Modal
        open
        title={title}
        onClose={onClose}
        footer={
          <>
            {deleteButton}
            <button type="button" className="btn" onClick={onClose}>
              {t('warehouse.receipts.header.close')}
            </button>
          </>
        }
      >
        {h.isOpen ? null : <p className="note rcp-modal-note">{t('warehouse.receipts.header.readOnlyNote')}</p>}
        <div className="r2">
          <ReadOnlyField label={t('warehouse.receipts.header.source')} value={doc ? receiptOriginText(h, t) : typeLabel} />
          <ReadOnlyField label={t('warehouse.receipts.fields.warehouse')} value={h.warehouseCode ?? ''} />
        </div>
        <div className="r2">
          <ReadOnlyField label={t('warehouse.receipts.header.mode')} value={h.receivingMode ?? receivingModeLabel(h.receivingModeCode, modeLookups)} />
          <ReadOnlyField label={t('warehouse.receipts.header.dock')} value={h.dockCode ?? '—'} />
        </div>
        {(!isDirectMode(h.receivingModeCode) || h.defaultStagingBinCode) && (
          <ReadOnlyField label={t('warehouse.receipts.fields.stagingBin')} value={h.defaultStagingBinCode ?? '—'} />
        )}
        <div className="r2">
          <ReadOnlyField label={t('warehouse.receipts.header.carrier')} value={h.carrier ?? '—'} />
          <ReadOnlyField label={t('warehouse.receipts.header.reference')} value={h.reference ?? '—'} />
        </div>
        {confirmDelete}
      </Modal>
    )
  }

  return (
    <Modal
      open
      size="lg"
      title={title}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          {deleteButton}
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : editing ? t('warehouse.receipts.header.save') : t('warehouse.receipts.create')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const carrier = v.carrier.trim()
          const reference = v.reference.trim()
          // en directo la posición de recepción no se pide (el campo está oculto)
          const stagingBinId = v.stagingBinId && !direct ? Number(v.stagingBinId) : null
          const dockId = v.dockId ? Number(v.dockId) : null
          try {
            if (!editing) {
              const manual = v.source === 'BLIND' || v.source === 'RETURN'
              const body: Schemas['ReceiptCreateRequest'] = {
                warehousePublicId: v.warehousePublicId,
                type: manual ? v.source : null,
                asnId: v.source === 'ASN' ? Number(v.asnId) : null,
                purchaseOrderPublicId: v.source === 'PO' ? v.purchaseOrderPublicId : null,
                stagingBinId,
                dockId,
                carrier: carrier || null,
                reference: reference || null,
                receivingMode: v.receivingMode || null,
              }
              const created = await create.mutateAsync(body)
              toast.success(t('warehouse.receipts.created', { number: created.header?.number ?? '' }))
              onCreated?.(created)
              onClose()
              return
            }
            // PATCH: solo lo que cambió (null = no cambiar; '' borra transporte o referencia)
            const body: Schemas['ReceiptHeaderUpdateRequest'] = {}
            if (sourceEditable && v.source !== initialSource) body.type = v.source
            if (warehouseEditable && v.warehousePublicId && v.warehousePublicId !== h?.warehousePublicId) body.warehousePublicId = v.warehousePublicId
            if (receivingModeChanged(h?.receivingModeCode, v.receivingMode)) body.receivingMode = normalizeReceivingMode(v.receivingMode)
            if (!direct && stagingBinId !== (h?.defaultStagingBinId ?? null)) {
              if (stagingBinId === null) body.clearStagingBin = true
              else body.stagingBinId = stagingBinId
            }
            if (dockId !== (h?.dockId ?? null)) {
              if (dockId === null) body.clearDock = true
              else body.dockId = dockId
            }
            if (carrier !== (h?.carrier ?? '')) body.carrier = carrier
            if (reference !== (h?.reference ?? '')) body.reference = reference
            if (Object.keys(body).length === 0) {
              onClose()
              return
            }
            const publicId = h?.publicId ?? ''
            const cached = qc.getQueryData<ReceiptDetailDto>([warehouseKeys.receipt[0], publicId])
            body.rowVersion = cached?.rowVersion ?? receipt?.rowVersion ?? null
            await update.mutateAsync({ publicId, body })
            toast.success(t('warehouse.receipts.header.saved', { number }))
            onClose()
          } catch (err) {
            throw remapProblemFields(err, (k) => (k.toLowerCase() === 'type' ? 'source' : null))
          }
        }}
      >
        {doc && <p className="note rcp-modal-note">{t('warehouse.receipts.header.documentNote')}</p>}
        {editing && !doc && !warehouseEditable && <p className="help rcp-modal-note">{t('warehouse.receipts.header.warehouseFixedNote')}</p>}
        <div className="r2">
          {sourceEditable && !preset ? (
            <Field name="source" label={t('warehouse.receipts.header.source')} required>
              <Select options={sourceOptions} />
            </Field>
          ) : (
            <ReadOnlyField
              label={t('warehouse.receipts.header.source')}
              value={h ? receiptOriginText(h, t) : t(`warehouse.receipts.sources.${initialSource}`)}
            />
          )}
          {warehouseEditable ? (
            <Field name="warehousePublicId" label={t('warehouse.receipts.fields.warehouse')} required>
              <WarehousePickerInput />
            </Field>
          ) : (
            <ReadOnlyField label={t('warehouse.receipts.fields.warehouse')} value={h?.warehouseCode ?? preset?.warehouseCode ?? ''} />
          )}
        </div>
        {!editing && source === 'ASN' && (
          <Field name="asnId" label={t('warehouse.receipts.fields.asn')} required help={warehousePublicId ? undefined : t('warehouse.receipts.pickWarehouseFirst')}>
            <ComboSelectInput
              options={asnOptions}
              loading={asns.isLoading}
              disabled={Boolean(preset) || !warehousePublicId}
              placeholder={t('warehouse.receipts.choose')}
            />
          </Field>
        )}
        {!editing && source === 'PO' && (
          <Field
            name="purchaseOrderPublicId"
            label={t('warehouse.receipts.fields.purchaseOrder')}
            required
            help={pos.error ? t('warehouse.receipts.noPoAccess') : warehousePublicId ? undefined : t('warehouse.receipts.pickWarehouseFirst')}
          >
            <ComboSelectInput options={poOptions} loading={pos.isLoading} disabled={!warehousePublicId} placeholder={t('warehouse.receipts.choose')} />
          </Field>
        )}
        <div className="r2">
          <Field name="receivingMode" label={t('warehouse.receipts.header.mode')} help={t('warehouse.receipts.header.modeHelp')}>
            <Select options={modeOptions} placeholder={editing ? undefined : t('warehouse.receipts.header.modeByWarehouse')} />
          </Field>
          <Field name="dockId" label={t('warehouse.receipts.header.dock')}>
            <ComboSelectInput options={dockOptions} loading={docks.isLoading} disabled={!warehousePublicId} placeholder={t('warehouse.receipts.header.noDock')} />
          </Field>
        </div>
        {!direct && (
          <Field name="stagingBinId" label={t('warehouse.receipts.fields.stagingBin')} help={t('warehouse.receipts.fields.stagingBinHelp')}>
            <BinPickerInput warehousePublicId={warehousePublicId} zoneTypeCodes={RECEIVING_ZONES} placeholder={t('warehouse.receipts.defaultStaging')} />
          </Field>
        )}
        <div className="r2">
          <Field name="carrier" label={t('warehouse.receipts.header.carrier')}>
            <TextInput maxLength={TEXT_MAX} autoComplete="off" />
          </Field>
          <Field name="reference" label={t('warehouse.receipts.header.reference')}>
            <TextInput maxLength={TEXT_MAX} autoComplete="off" />
          </Field>
        </div>
      </Form>
      {confirmDelete}
    </Modal>
  )
}
