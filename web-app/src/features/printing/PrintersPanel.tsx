// Panel «Impresoras de la compañía» (Lote F-G1; la maqueta no lo tiene, pero sin él no hay qué elegir en las estaciones): tabla local con
// Nombre, Nombre en el sistema, Modelo, Uso, Tamaño, Conexión, Girar, Predeterminada y Estado; «Incluir inactivas»; alta y edición en
// un modal (el uso se fija al crear); hacer predeterminada, desactivar y reactivar. Todo con printers.manage.
import { zodResolver } from '@hookform/resolvers/zod'
import { useId, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  Field,
  FilterScope,
  IconCheckCircle,
  IconEdit,
  IconPlus,
  IconPower,
  IconRotateCcw,
  matchesQ,
  Panel,
  QBox,
  Select,
  Spinner,
  TextArea,
  TextInput,
  Toggle,
  toast,
  type DataColumn,
} from '../../kernel/ui'
import { IconPrint } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { buildPrinterRequest, LABEL_MEDIA_OPTIONS, printerSchema, printerValuesOf, type PrinterValues } from './printerRules'
import { usePrinterMutations, usePrinters, type PrinterDto } from './printingApi'

const NO_ROWS: PrinterDto[] = []

function PrinterModal({ printer, detected, onClose }: { printer: PrinterDto | null; detected: readonly string[]; onClose: () => void }) {
  const t = useT()
  const listId = useId()
  const { create, update } = usePrinterMutations()
  const schema = useMemo(() => printerSchema(t), [t])
  const form = useForm<PrinterValues>({ resolver: zodResolver(schema), defaultValues: printerValuesOf(printer) })
  const purpose = useWatch({ control: form.control, name: 'purpose' })
  const connection = useWatch({ control: form.control, name: 'connectionType' })
  const mediaOptions = purpose === 'DOCUMENT' ? [{ value: 'LETTER', label: t('printing.media.LETTER') }] : LABEL_MEDIA_OPTIONS.map((m) => ({ value: m, label: t(`printing.media.${m}`) }))
  return (
    <FormModal
      id="printer"
      size="md"
      title={printer ? t('printing.printer.edit') : t('printing.printer.new')}
      open
      onClose={onClose}
      form={form}
      onSubmit={async (v: PrinterValues) => {
        if (printer) await update.mutateAsync({ publicId: printer.publicId!, body: buildPrinterRequest(v, printer) })
        else await create.mutateAsync(buildPrinterRequest(v, null))
        toast.success(t(printer ? 'printing.printer.saved' : 'printing.printer.created', { name: v.name.trim() }))
        onClose()
      }}
    >
      <Field name="name" label={t('printing.printer.name')} required help={t('printing.printer.nameHelp')}>
        <TextInput maxLength={80} autoComplete="off" placeholder="Zebra ZD420 — Mostrador 1" />
      </Field>
      <div className="r2">
        {printer ? (
          <div className="f">
            <label htmlFor="prn-purpose-ro">{t('printing.printer.purpose')}</label>
            <input id="prn-purpose-ro" value={t(`printing.purpose.${printer.purpose}`)} disabled readOnly />
            <p className="help">{t('printing.printer.purposeFixed')}</p>
          </div>
        ) : (
          <Field name="purpose" label={t('printing.printer.purpose')} required>
            <Select
              options={[
                { value: 'LABEL', label: t('printing.purpose.LABEL') },
                { value: 'DOCUMENT', label: t('printing.purpose.DOCUMENT') },
              ]}
            />
          </Field>
        )}
        <Field name="mediaSize" label={t('printing.printer.media')}>
          <Select key={purpose} options={mediaOptions} />
        </Field>
      </div>
      <Field name="connectionType" label={t('printing.printer.connection')} help={t('printing.printer.connectionHelp')}>
        <Select
          options={[
            { value: 'QZ_TRAY', label: t('printing.connection.QZ_TRAY') },
            { value: 'BROWSER', label: t('printing.connection.BROWSER') },
          ]}
        />
      </Field>
      {connection === 'QZ_TRAY' && (
        <Field name="systemName" label={t('printing.printer.systemName')} help={t('printing.printer.systemNameHelp')}>
          <TextInput maxLength={200} autoComplete="off" list={listId} />
        </Field>
      )}
      <datalist id={listId}>
        {detected.map((d) => (
          <option key={d} value={d} />
        ))}
      </datalist>
      <Field name="model" label={t('printing.printer.model')}>
        <TextInput maxLength={80} autoComplete="off" placeholder="ZD420" />
      </Field>
      <Field name="rotateMedia" label={t('printing.printer.rotate')} help={t('printing.printer.rotateHelp')}>
        <Toggle text={t('printing.printer.rotateText')} />
      </Field>
      <Field name="notes" label={t('printing.printer.notes')}>
        <TextArea rows={2} maxLength={300} />
      </Field>
    </FormModal>
  )
}

export function PrintersPanel({ detected }: { detected: readonly string[] }) {
  const t = useT()
  const [includeInactive, setIncludeInactive] = useState(false)
  const [q, setQ] = useState('')
  const list = usePrinters(includeInactive)
  const { setActive, makeDefault } = usePrinterMutations()
  const [editing, setEditing] = useState<{ printer: PrinterDto | null } | null>(null)
  const [deactivating, setDeactivating] = useState<PrinterDto | null>(null)
  const rows = useMemo(() => (list.data ?? NO_ROWS).filter((p) => matchesQ(q, p.name, p.systemName, p.model, p.notes)), [list.data, q])

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<PrinterDto>[]>(
    () => [
      { id: 'name', header: t('printing.printer.name'), cell: (p) => <b>{p.name}</b>, sortValue: (p) => p.name ?? '', card: 'title' },
      { id: 'systemName', header: t('printing.printer.systemNameShort'), cell: (p) => p.systemName || '—', sortValue: (p) => p.systemName ?? '' },
      { id: 'model', header: t('printing.printer.model'), cell: (p) => p.model || '—', sortValue: (p) => p.model ?? '' },
      { id: 'purpose', header: t('printing.printer.purpose'), cell: (p) => t(`printing.purpose.${p.purpose}`), sortValue: (p) => t(`printing.purpose.${p.purpose}`) },
      { id: 'media', header: t('printing.printer.media'), cell: (p) => t(`printing.media.${p.mediaSize}`), sortValue: (p) => p.mediaSize ?? '' },
      { id: 'connection', header: t('printing.printer.connection'), cell: (p) => t(`printing.connection.${p.connectionType}`), sortValue: (p) => p.connectionType ?? '' },
      { id: 'rotate', header: t('printing.printer.rotateShort'), cell: (p) => (p.rotateMedia ? t('printing.yes') : t('printing.no')), sortValue: (p) => (p.rotateMedia ? 1 : 0) },
      {
        id: 'default',
        header: t('printing.printer.default'),
        cell: (p) => (p.isDefault ? <Chip tone="deliv">{t('printing.printer.defaultChip')}</Chip> : '—'),
        exportValue: (p) => (p.isDefault ? t('printing.yes') : ''),
        sortValue: (p) => (p.isDefault ? 1 : 0),
      },
      {
        id: 'active',
        header: t('printing.printer.state'),
        cell: (p) => (p.isActive ? <Chip tone="deliv">{t('printing.active')}</Chip> : <Chip tone="cap">{t('printing.inactive')}</Chip>),
        exportValue: (p) => (p.isActive ? t('printing.active') : t('printing.inactive')),
        sortValue: (p) => (p.isActive ? 1 : 0),
      },
    ],
    [t],
  )

  return (
    <FilterScope>
      <Panel
        flush
        icon={<IconPrint />}
        title={t('printing.printer.title')}
        badge={list.isPending ? undefined : rows.length}
        actions={
          <button type="button" className="btn sm flow" onClick={() => setEditing({ printer: null })}>
            <IconPlus /> {t('printing.printer.new')}
          </button>
        }
      >
        <div className="pb" style={{ paddingBottom: 0 }}>
          <div className="prn-bar">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
            <label className="sw">
              <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('printing.printer.includeInactive')}</span>
            </label>
          </div>
        </div>
        <div className="pb" style={{ paddingTop: 10 }}>
          {list.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('printing.printer.title')}
              columns={columns}
              rows={rows}
              rowKey={(p) => p.publicId ?? ''}
              defaultSort={{ id: 'name', desc: false }}
              rowClassName={(p) => (p.isActive ? undefined : 'dim')}
              empty={t('printing.printer.empty')}
              rowActions={[
                { key: 'edit', label: t('printing.printer.edit'), icon: <IconEdit />, onClick: (p) => setEditing({ printer: p }), visible: (p) => !!p.isActive },
                {
                  key: 'default',
                  label: t('printing.printer.makeDefault'),
                  icon: <IconCheckCircle />,
                  onClick: (p) => void run(makeDefault.mutateAsync(p.publicId!), t('printing.printer.madeDefault', { name: p.name ?? '' })),
                  visible: (p) => !!p.isActive && !p.isDefault,
                },
                { key: 'deactivate', label: t('printing.printer.deactivate'), icon: <IconPower />, tone: 'danger', onClick: (p) => setDeactivating(p), visible: (p) => !!p.isActive },
                {
                  key: 'reactivate',
                  label: t('printing.printer.reactivate'),
                  icon: <IconRotateCcw />,
                  onClick: (p) => void run(setActive.mutateAsync({ publicId: p.publicId!, active: true }), t('printing.printer.reactivated', { name: p.name ?? '' })),
                  visible: (p) => !p.isActive,
                },
              ]}
            />
          )}
        </div>
        {editing && <PrinterModal key={editing.printer?.publicId ?? 'new'} printer={editing.printer} detected={detected} onClose={() => setEditing(null)} />}
        <ConfirmDialog
          open={deactivating !== null}
          tone="danger"
          title={t('printing.printer.deactivateTitle')}
          message={t(deactivating?.isDefault ? 'printing.printer.deactivateDefaultBody' : 'printing.printer.deactivateBody', { name: deactivating?.name ?? '' })}
          confirmLabel={t('printing.printer.deactivate')}
          onConfirm={async () => {
            if (!deactivating) return
            await setActive.mutateAsync({ publicId: deactivating.publicId!, active: false })
            toast.success(t('printing.printer.deactivated', { name: deactivating.name ?? '' }))
          }}
          onClose={() => setDeactivating(null)}
        />
      </Panel>
    </FilterScope>
  )
}
