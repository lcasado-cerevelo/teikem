// Panel «Construir el label» (maqueta `labelBuilderPanelHtml`): qué elementos aparecen, su posición en las 9 zonas y su tamaño; waybill y
// factura además «como código de barras». La vista previa (`LabelPreview`) se actualiza al momento y es la misma que sale en el PDF.
// Trabaja sobre las PLANTILLAS de la compañía (printers.manage): elegir, Nueva, Duplicar, Hacer predeterminada, Desactivar/Reactivar
// y «Actualizar construcción del label» (PATCH con rowVersion; con el label integrado elegido, «Guardar como plantilla»).
import { useId, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { useFormat } from '../../kernel/format'
import { Modal, Panel, toast } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import {
  BARCODE_CAPABLE,
  BUILT_IN_LAYOUT,
  hasVisibleBarcode,
  LABEL_KEYS,
  LABEL_POSITIONS,
  LABEL_SIZES,
  normalizeLayout,
  sameLayout,
  sampleLabelData,
  type LabelKey,
  type LabelLayout,
  type LabelPos,
  type LabelSize,
} from '../../kernel/ui/orderLabelLayout'
import { LabelPreview } from './LabelPreview'
import { BUILT_IN_ID, initialTemplateId } from './printerRules'
import { useLabelTemplates, useTemplateMutations } from './printingApi'

function NameModal({ title, initial, onClose, onSave }: { title: string; initial: string; onClose: () => void; onSave: (name: string) => Promise<void> }) {
  const t = useT()
  const id = useId()
  const [name, setName] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    if (!name.trim()) {
      setError(t('printing.builder.nameRequired'))
      return
    }
    setBusy(true)
    try {
      await onSave(name.trim())
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      title={title}
      size="sm"
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={`${id}-f`} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <form
        id={`${id}-f`}
        onSubmit={(e) => {
          e.preventDefault()
          e.stopPropagation()
          void submit()
        }}
      >
        <div className="f">
          <label htmlFor={id}>{t('printing.builder.name')}</label>
          <input id={id} value={name} maxLength={80} autoComplete="off" aria-invalid={error ? true : undefined} onChange={(e) => setName(e.target.value)} />
          {error && (
            <p className="ferr" role="alert">
              {error}
            </p>
          )}
        </div>
      </form>
    </Modal>
  )
}

export interface LabelBuilderPanelProps {
  /** Tamaño de la vista previa (el de la impresora de labels de esta estación). */
  mediaSize: string
  company: string
}

export function LabelBuilderPanel({ mediaSize, company }: LabelBuilderPanelProps) {
  const t = useT()
  const f = useFormat()
  const selectId = useId()
  const list = useLabelTemplates(true)
  const templates = useMemo(() => list.data ?? [], [list.data])
  const m = useTemplateMutations()
  const [picked, setSelected] = useState<string | null>(null)
  // al cargar, la predeterminada (o la primera activa, o el integrado)
  const selected = picked ?? (list.data ? initialTemplateId(list.data) : null)
  const [naming, setNaming] = useState<null | 'new' | 'duplicate' | 'saveAs'>(null)

  const current = templates.find((x) => x.publicId === selected) ?? null
  const base = useMemo(() => (current ? normalizeLayout(current.layout) : BUILT_IN_LAYOUT), [current])
  // lo editado vale mientras siga siendo de la misma plantilla y su construcción guardada no cambie (al guardar o cambiar de
  // plantilla vuelve a la guardada)
  const [edit, setEdit] = useState<{ id: string | null; base: LabelLayout; layout: LabelLayout } | null>(null)
  const draft = edit && edit.id === selected && sameLayout(edit.base, base) ? edit.layout : base
  const setDraft = (next: LabelLayout | ((d: LabelLayout) => LabelLayout)) =>
    setEdit({ id: selected, base, layout: typeof next === 'function' ? next(draft) : next })

  const dirty = !sameLayout(draft, base)
  const noBarcode = !hasVisibleBarcode(draft)
  const sample = useMemo(() => sampleLabelData(company, f.money(125.82)), [company, f])
  const setEl = (key: LabelKey, patch: Partial<LabelLayout[LabelKey]>) => setDraft((d) => ({ ...d, [key]: { ...d[key], ...patch } }))

  const fail = (e: unknown) => toast.error(applyProblemDetails(e).title)
  const save = async () => {
    if (!current) {
      setNaming('saveAs')
      return
    }
    try {
      await m.update.mutateAsync({ publicId: current.publicId!, layout: draft, rowVersion: current.rowVersion })
      toast.success(t('printing.builder.updated'))
    } catch (e) {
      fail(e)
    }
  }
  const create = async (name: string, layout: LabelLayout) => {
    const created = await m.create.mutateAsync({ name, layout })
    setSelected(created.publicId ?? null)
    toast.success(t('printing.builder.created', { name }))
  }

  return (
    <Panel icon={<IconTag />} title={t('printing.builder.title')}>
      <p className="help" style={{ marginTop: 0 }}>
        {t('printing.builder.hint')}
      </p>
      <div className="prn-tplbar">
        <div className="f">
          <label htmlFor={selectId}>{t('printing.builder.template')}</label>
          <select id={selectId} value={selected ?? BUILT_IN_ID} onChange={(e) => setSelected(e.target.value)}>
            {templates.map((x) => (
              <option key={x.publicId} value={x.publicId}>
                {x.name}
                {x.isDefault ? ` · ${t('printing.builder.isDefault')}` : ''}
                {x.isActive ? '' : ` · ${t('printing.builder.inactive')}`}
              </option>
            ))}
            <option value={BUILT_IN_ID}>{t('printing.station.builtInTemplate')}</option>
          </select>
        </div>
        <div className="prn-acts">
          <button type="button" className="btn sm" onClick={() => setNaming('new')}>
            {t('printing.builder.new')}
          </button>
          <button type="button" className="btn sm" onClick={() => setNaming('duplicate')}>
            {t('printing.builder.duplicate')}
          </button>
          {current?.isActive && !current.isDefault && (
            <button
              type="button"
              className="btn sm"
              onClick={() =>
                void m.makeDefault
                  .mutateAsync(current.publicId!)
                  .then(() => toast.success(t('printing.builder.madeDefault', { name: current.name ?? '' })))
                  .catch(fail)
              }
            >
              {t('printing.builder.makeDefault')}
            </button>
          )}
          {current && (
            <button
              type="button"
              className={`btn sm${current.isActive ? ' danger' : ''}`}
              onClick={() =>
                void m.setActive
                  .mutateAsync({ publicId: current.publicId!, active: !current.isActive })
                  .then(() => toast.success(t(current.isActive ? 'printing.builder.deactivated' : 'printing.builder.reactivated', { name: current.name ?? '' })))
                  .catch(fail)
              }
            >
              {current.isActive ? t('printing.builder.deactivate') : t('printing.builder.reactivate')}
            </button>
          )}
        </div>
      </div>

      <table className="lst densetbl lbl-build">
        <thead>
          <tr>
            <th>{t('printing.builder.on')}</th>
            <th>{t('printing.builder.element')}</th>
            <th>{t('printing.builder.position')}</th>
            <th>{t('printing.builder.size')}</th>
            <th>{t('printing.builder.asBarcode')}</th>
          </tr>
        </thead>
        <tbody>
          {LABEL_KEYS.map((key) => {
            const el = draft[key]
            const name = t(`printing.element.${key}`)
            return (
              <tr key={key} data-el={key}>
                <td>
                  <input type="checkbox" checked={el.on} aria-label={t('printing.builder.showOf', { name })} onChange={(e) => setEl(key, { on: e.target.checked })} />
                </td>
                <td className="lbl-name">{name}</td>
                <td>
                  <select aria-label={t('printing.builder.positionOf', { name })} value={el.pos} onChange={(e) => setEl(key, { pos: e.target.value as LabelPos })}>
                    {LABEL_POSITIONS.map((p) => (
                      <option key={p} value={p}>
                        {t(`printing.pos.${p}`)}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  <select aria-label={t('printing.builder.sizeOf', { name })} value={el.size} onChange={(e) => setEl(key, { size: e.target.value as LabelSize })}>
                    {LABEL_SIZES.map((s) => (
                      <option key={s} value={s}>
                        {t(`printing.size.${s}`)}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  {BARCODE_CAPABLE.includes(key) ? (
                    <input type="checkbox" checked={el.asBarcode} aria-label={t('printing.builder.asBarcodeOf', { name })} onChange={(e) => setEl(key, { asBarcode: e.target.checked })} />
                  ) : (
                    <span className="lbl-na">—</span>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      <div className="prn-preview-wrap">
        <LabelPreview layout={draft} data={sample} mediaSize={mediaSize} />
      </div>
      {noBarcode && (
        <p className="ferr" role="alert">
          {t('printing.builder.needsBarcode')}
        </p>
      )}
      <div className="prn-acts">
        <button type="button" className="btn flow" disabled={noBarcode || m.update.isPending || (!!current && !dirty)} onClick={() => void save()}>
          {current ? t('printing.builder.update') : t('printing.builder.saveAs')}
        </button>
        {dirty && (
          <button type="button" className="btn" onClick={() => setDraft(base)}>
            {t('printing.builder.discard')}
          </button>
        )}
      </div>

      {naming && (
        <NameModal
          title={naming === 'new' ? t('printing.builder.new') : naming === 'duplicate' ? t('printing.builder.duplicate') : t('printing.builder.saveAs')}
          initial={naming === 'duplicate' ? t('printing.builder.copyOf', { name: current?.name ?? t('printing.station.builtInTemplate') }) : ''}
          onClose={() => setNaming(null)}
          onSave={(name) => create(name, naming === 'new' ? BUILT_IN_LAYOUT : draft)}
        />
      )}
    </Panel>
  )
}
