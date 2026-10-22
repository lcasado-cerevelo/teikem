// Panel «Esta estación — labels» / «Esta estación — documentos» (maqueta `printerConfigPanelHtml(kind)`): impresora por defecto,
// template de label (o formato de documento: solo Carta, D-FG7), «Imprimir al guardar» y «Confirmar antes», Prueba y Guardar. Lo
// elegido vive en este navegador (`stationConfig.ts`); «Guardar» lo deja guardado. Con una impresora de conexión QZ Tray se elige
// además la impresora de ESTA computadora (detectada con QZ Tray) por su nombre exacto.
import { useId, useMemo, useState } from 'react'
import { useT } from '../../kernel/i18n'
import { Modal, Panel, toast } from '../../kernel/ui'
import { IconPrint } from '../../kernel/ui/screenIcons'
import { sampleLabelData, type OrderLabelData } from '../../kernel/ui/orderLabelLayout'
import type { PrintOutcome } from '../../kernel/ui/printPdf'
import { useFormat } from '../../kernel/format'
import { LabelPreview } from './LabelPreview'
import { useLabelTexts } from './labelTexts'
import { labelPdfFactory, outcomeMessage, printWith, testDocumentPdf } from './printActions'
import type { StationOptions } from './printingApi'
import {
  BUILT_IN,
  COMPANY_DEFAULT,
  PDF_ONLY,
  resolvePrinter,
  resolveTemplate,
  type StationConfig,
} from './stationConfig'

export interface StationPrintPanelProps {
  kind: 'label' | 'doc'
  options: StationOptions | undefined
  saved: StationConfig
  onSave: (next: StationConfig) => void
  /** Impresoras detectadas en esta computadora con QZ Tray (vacío = no se ha detectado). */
  detected: readonly string[]
  company: string
}

function mediaText(t: (k: string) => string, media: string | null | undefined) {
  return media ? t(`printing.media.${media}`) : ''
}

export function StationPrintPanel({ kind, options, saved, onSave, detected, company }: StationPrintPanelProps) {
  const t = useT()
  const f = useFormat()
  const ids = { printer: useId(), template: useId(), system: useId() }
  const isLabel = kind === 'label'
  // borrador de ESTE panel: vale mientras lo guardado de su parte no cambie (guardar el otro panel no lo pisa)
  const part = (c: StationConfig) => JSON.stringify(isLabel ? c.label : c.doc)
  const [edit, setEdit] = useState<{ base: string; draft: StationConfig } | null>(null)
  const draft: StationConfig = edit && edit.base === part(saved) ? { ...saved, [kind]: isLabel ? edit.draft.label : edit.draft.doc } : saved
  const setDraft = (fn: (d: StationConfig) => StationConfig) => setEdit({ base: part(saved), draft: fn(draft) })
  const [preview, setPreview] = useState(false)
  const [busy, setBusy] = useState(false)
  const texts = useLabelTexts()

  const own = isLabel ? draft.label : draft.doc
  const purpose = isLabel ? 'LABEL' : 'DOCUMENT'
  const printers = useMemo(() => (options?.printers ?? []).filter((p) => p.purpose === purpose), [options, purpose])
  const companyDefault = printers.find((p) => p.isDefault)
  const resolved = resolvePrinter(kind, draft, options)
  const template = resolveTemplate(draft, options)
  const templates = options?.templates ?? []
  const companyTemplate = templates.find((x) => x.isDefault)
  const dirty = part(draft) !== part(saved)
  const sample: OrderLabelData = useMemo(() => sampleLabelData(company, f.money(125.82)), [company, f])

  const setOwn = (patch: Partial<StationConfig['label']> & Partial<StationConfig['doc']>) =>
    setDraft((d) => (isLabel ? { ...d, label: { ...d.label, ...patch } } : { ...d, doc: { ...d.doc, ...patch } }))

  const report = (o: PrintOutcome, notFitting = 0) => {
    const m = outcomeMessage(o, t)
    if (notFitting > 0) toast.info(t('printing.notFit', { size: mediaText(t, resolved.mediaSize) }))
    if (m.tone === 'success') toast.success(m.text)
    else toast.info(m.text)
  }

  const runTest = async () => {
    setBusy(true)
    try {
      if (isLabel) {
        const factory = labelPdfFactory(t('printing.test.labelTitle'), resolved, template.layout, texts, [sample])
        report(await printWith(resolved, factory.make, `${t('printing.test.labelFile')}.pdf`), factory.notFitting())
      } else {
        report(await printWith(resolved, () => testDocumentPdf(t, company, f.dateTime(new Date().toISOString())), `${t('printing.test.docFile')}.pdf`))
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
      setPreview(false)
    }
  }

  const download = async () => {
    setBusy(true)
    try {
      if (isLabel) {
        const factory = labelPdfFactory(t('printing.test.labelTitle'), resolved, template.layout, texts, [sample])
        ;(await factory.make(true)).save(`${t('printing.test.labelFile')}.pdf`)
        if (factory.notFitting() > 0) toast.info(t('printing.notFit', { size: mediaText(t, resolved.mediaSize) }))
      } else (await testDocumentPdf(t, company, f.dateTime(new Date().toISOString()))).save(`${t('printing.test.docFile')}.pdf`)
    } finally {
      setBusy(false)
      setPreview(false)
    }
  }

  const via =
    resolved.connection === 'QZ_TRAY' && resolved.systemName
      ? t('printing.station.viaQz', { name: resolved.systemName })
      : resolved.printer
        ? t('printing.station.viaBrowserPrinter', { name: resolved.printer.name ?? '' })
        : t('printing.station.viaBrowser')

  const systemChoices = useMemo(() => {
    const list = [...detected]
    if (own.systemPrinter && !list.includes(own.systemPrinter)) list.unshift(own.systemPrinter)
    return list
  }, [detected, own.systemPrinter])

  return (
    <Panel icon={<IconPrint />} title={isLabel ? t('printing.station.label') : t('printing.station.doc')}>
      <div className="f">
        <label htmlFor={ids.printer}>{t('printing.station.printer')}</label>
        <select id={ids.printer} value={own.printer} onChange={(e) => setOwn({ printer: e.target.value })}>
          <option value={COMPANY_DEFAULT}>
            {companyDefault ? t('printing.station.companyDefault', { name: companyDefault.name ?? '' }) : t('printing.station.companyNone')}
          </option>
          {printers.map((p) => (
            <option key={p.publicId} value={p.publicId}>
              {`${p.name} · ${mediaText(t, p.mediaSize)}${p.connectionType === 'QZ_TRAY' ? ` · ${t('printing.connection.QZ_TRAY')}` : ''}`}
            </option>
          ))}
          <option value={PDF_ONLY}>{t('printing.station.pdfOnly')}</option>
        </select>
      </div>
      {resolved.connection === 'QZ_TRAY' && (
        <div className="f">
          <label htmlFor={ids.system}>{t('printing.station.systemPrinter')}</label>
          <select id={ids.system} value={own.systemPrinter} onChange={(e) => setOwn({ systemPrinter: e.target.value })}>
            <option value="">{t('printing.station.systemPrinterDefault', { name: resolved.printer?.systemName || resolved.printer?.name || '' })}</option>
            {systemChoices.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
          <p className="help">{detected.length > 0 ? t('printing.station.systemPrinterHelp') : t('printing.station.systemPrinterDetect')}</p>
        </div>
      )}
      <div className="f">
        <label htmlFor={ids.template}>{isLabel ? t('printing.station.template') : t('printing.station.format')}</label>
        {isLabel ? (
          <select id={ids.template} value={draft.label.template} onChange={(e) => setOwn({ template: e.target.value })}>
            <option value={COMPANY_DEFAULT}>
              {companyTemplate ? t('printing.station.companyDefault', { name: companyTemplate.name ?? '' }) : t('printing.station.builtInTemplate')}
            </option>
            {templates.map((x) => (
              <option key={x.publicId} value={x.publicId}>
                {x.name}
              </option>
            ))}
            <option value={BUILT_IN}>{t('printing.station.builtInTemplate')}</option>
          </select>
        ) : (
          <select id={ids.template} value="LETTER" disabled>
            <option value="LETTER">{t('printing.station.letter')}</option>
          </select>
        )}
      </div>
      {resolved.notice === 'printerInactive' && <p className="note">{t('printing.station.printerInactive')}</p>}
      {isLabel && template.notice === 'templateInactive' && <p className="note">{t('printing.station.templateInactive')}</p>}
      <div className="r2">
        <label className="sw">
          <input type="checkbox" role="switch" checked={own.confirm} onChange={(e) => setOwn({ confirm: e.target.checked })} />
          <span className="tk" aria-hidden="true" />
          <span>{t('printing.station.confirm')}</span>
        </label>
      </div>
      {isLabel && <p className="help">{t('printing.station.perClient')}</p>}
      <p className="help prn-via">{via}</p>
      <div className="prn-acts">
        <button type="button" className="btn" disabled={busy} onClick={() => (own.confirm ? setPreview(true) : void runTest())}>
          <IconPrint /> {isLabel ? t('printing.station.test') : t('printing.station.testDoc')}
        </button>
        <button
          type="button"
          className="btn flow"
          disabled={!dirty}
          onClick={() => {
            onSave(draft)
            toast.success(t('printing.station.saved'))
          }}
        >
          {t('printing.station.save')}
        </button>
      </div>
      <Modal
        open={preview}
        title={isLabel ? t('printing.preview.labelTitle') : t('printing.preview.docTitle')}
        onClose={() => setPreview(false)}
        dismissible={!busy}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setPreview(false)} disabled={busy}>
              {t('common.cancel')}
            </button>
            <button type="button" className="btn" onClick={() => void download()} disabled={busy}>
              {t('printing.preview.download')}
            </button>
            <button type="button" className="btn flow" onClick={() => void runTest()} disabled={busy}>
              <IconPrint /> {t('printing.preview.print')}
            </button>
          </>
        }
      >
        <p className="help">{via}</p>
        {isLabel ? (
          <div className="prn-preview-wrap">
            <LabelPreview layout={template.layout} data={sample} mediaSize={resolved.mediaSize} />
          </div>
        ) : (
          <p>{t('printing.test.docBody')}</p>
        )}
      </Modal>
    </Panel>
  )
}
