// Lote F-G2 — imprimir labels y documentos desde cualquier pantalla con la configuración de ESTA estación (motor de F-G1):
// - labels: impresora de labels y plantilla de la estación (→ compañía → integrado); directo con QZ Tray si la impresora es de esa
//   conexión y QZ Tray está conectado, si no el PDF y el diálogo del navegador (o descarga en un teléfono);
// - documentos (waybill summary, packing list, informe de faltantes): la impresora de documentos de la estación, en carta.
// Con «Confirmar antes» se abre la vista previa (Imprimir / Descargar PDF / Cancelar). Avisa las órdenes canceladas y las piezas sin
// número de paquete que no se imprimieron, y desde 500 labels que el PDF será grande (sin tope). Un fallo de impresión nunca rompe
// la pantalla; después de guardar una orden se avisa «La orden se guardó, pero no se pudo imprimir el label…».
import type { jsPDF } from 'jspdf'
import { useContext, useState, type ReactNode } from 'react'
import { SessionContext } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { IconPrint } from '../../kernel/ui/screenIcons'
import { renderReportPdf, type ReportSpec } from '../../kernel/ui/reportPdf'
import type { PrintOutcome } from '../../kernel/ui/printPdf'
import { LabelPreview } from './LabelPreview'
import { useLabelTexts } from './labelTexts'
import type { LabelKind } from '../../kernel/ui/orderLabelLayout'
import { buildInvoiceLabelJob, buildLabelJob, labelFileBase, type LabelJob, type LabelOrder } from './orderLabels'
import { labelPdfFactory, outcomeMessage, printWith } from './printActions'
import { useStationOptions } from './printingApi'
import { readStationConfig, resolveInvoiceTemplate, resolvePrinter, resolveTemplate, type ResolvedPrinter, type ResolvedTemplate } from './stationConfig'

type Pending =
  | { kind: 'labels'; labelKind: LabelKind; job: LabelJob; fileBase: string; printer: ResolvedPrinter; template: ResolvedTemplate }
  | { kind: 'doc'; title: string; fileBase: string; printer: ResolvedPrinter; make: () => Promise<jsPDF> }

export interface PrintLabelsOptions {
  /** Después de guardar una orden: un fallo avisa que la orden sí se guardó. */
  afterSave?: boolean
  /** Después de los labels de las piezas, imprime el label de FACTURA de cada orden (cliente con «label de factura»). Nunca pregunta. */
  invoice?: boolean
}

export function usePrinting() {
  const t = useT()
  const f = useFormat()
  // sin sesión (pruebas de una pantalla suelta) imprime igual: estación «sin compañía» y logo vacío
  const session = useContext(SessionContext)
  const tenantId = session?.tenantId ?? null
  const me = session?.me
  const options = useStationOptions()
  const texts = useLabelTexts()
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  const company = me?.tenantName ?? ''

  const report = (o: PrintOutcome) => {
    const m = outcomeMessage(o, t)
    if (m.tone === 'success') toast.success(m.text)
    else toast.info(m.text)
  }
  const fail = (e: unknown, afterSave?: boolean) => {
    const reason = e instanceof Error ? e.message : applyProblemDetails(e).title
    toast.error(afterSave ? t('printing.jobs.afterSaveFailed', { reason }) : t('printing.jobs.failed', { reason }))
  }

  async function runLabels(p: Extract<Pending, { kind: 'labels' }>, afterSave?: boolean) {
    setBusy(true)
    try {
      const factory = labelPdfFactory(t('printing.jobs.labelsTitle'), p.printer, p.template.layout, texts, p.job.labels, p.labelKind)
      report(await printWith(p.printer, factory.make, `${p.fileBase}.pdf`))
      if (factory.notFitting() > 0) toast.info(t('printing.notFit', { size: t(`printing.media.${p.printer.mediaSize}`) }))
    } catch (e) {
      fail(e, afterSave)
    } finally {
      setBusy(false)
    }
  }

  async function runDoc(p: Extract<Pending, { kind: 'doc' }>) {
    setBusy(true)
    try {
      report(await printWith(p.printer, () => p.make(), `${p.fileBase}.pdf`))
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  /** Imprime los labels de las órdenes (uno por pieza). Devuelve cuántos labels se mandaron (o se abrieron en la vista previa). */
  async function printLabels(orders: readonly LabelOrder[], opts: PrintLabelsOptions = {}): Promise<number> {
    try {
      const job = buildLabelJob(orders, company, (n) => f.money(n))
      if (job.cancelled > 0) toast.info(t('printing.jobs.cancelled', { n: job.cancelled }))
      if (job.withoutNumber > 0) toast.info(t('printing.jobs.withoutNumber', { n: job.withoutNumber }))
      if (job.labels.length === 0) {
        toast.info(t('printing.jobs.none'))
        if (opts.invoice) await printInvoiceLabels(orders, opts)
        return 0
      }
      if (job.large) toast.info(t('printing.jobs.large', { n: job.labels.length }))
      const config = readStationConfig(tenantId)
      const p: Extract<Pending, { kind: 'labels' }> = {
        kind: 'labels',
        labelKind: 'PIECE',
        job,
        fileBase: labelFileBase(orders, job.labels.length),
        printer: resolvePrinter('label', config, options.data),
        template: resolveTemplate(config, options.data),
      }
      // «al guardar» (label automático del cliente o «Label al guardar»): sale solo, sin vista previa ni pregunta aunque la estación esté en «confirmar»; el resto de las impresiones sí confirma
      if (config.label.confirm && !opts.afterSave) setPending(p)
      else await runLabels(p, opts.afterSave)
      if (opts.invoice) await printInvoiceLabels(orders, opts)
      return job.labels.length
    } catch (e) {
      fail(e, opts.afterSave)
      return 0
    }
  }

  /**
   * Imprime el label de FACTURA (uno por orden con número de factura) en la impresora de labels de la estación y con la plantilla de factura de la compañía
   * (o el integrado: código de barras del número con el número debajo). Si es «al guardar» sale solo, sin vista previa ni pregunta.
   */
  async function printInvoiceLabels(orders: readonly LabelOrder[], opts: PrintLabelsOptions = {}): Promise<number> {
    try {
      const job = buildInvoiceLabelJob(orders, company, (n) => f.money(n))
      if (job.withoutInvoice > 0) toast.info(t('printing.jobs.withoutInvoice', { n: job.withoutInvoice }))
      if (job.labels.length === 0) return 0
      const config = readStationConfig(tenantId)
      const p: Extract<Pending, { kind: 'labels' }> = {
        kind: 'labels',
        labelKind: 'INVOICE',
        job,
        fileBase: `factura-${job.labels.length === 1 ? job.labels[0].invoice : job.labels.length}`,
        printer: resolvePrinter('label', config, options.data),
        template: resolveInvoiceTemplate(options.data),
      }
      if (config.label.confirm && !opts.afterSave) setPending(p)
      else await runLabels(p, opts.afterSave)
      return job.labels.length
    } catch (e) {
      fail(e, opts.afterSave)
      return 0
    }
  }

  /** Imprime un documento (carta) con la impresora de documentos de la estación. */
  async function printDocument(spec: ReportSpec, fileBase: string): Promise<void> {
    const config = readStationConfig(tenantId)
    const p: Extract<Pending, { kind: 'doc' }> = {
      kind: 'doc',
      title: spec.title,
      fileBase,
      printer: resolvePrinter('doc', config, options.data),
      make: () => renderReportPdf({ ...spec, company: spec.company ?? company }),
    }
    if (config.doc.confirm) setPending(p)
    else await runDoc(p)
  }

  async function download() {
    if (!pending) return
    setBusy(true)
    try {
      if (pending.kind === 'labels') {
        const factory = labelPdfFactory(t('printing.jobs.labelsTitle'), pending.printer, pending.template.layout, texts, pending.job.labels, pending.labelKind)
        ;(await factory.make(true)).save(`${pending.fileBase}.pdf`)
      } else (await pending.make()).save(`${pending.fileBase}.pdf`)
      setPending(null)
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  const via = (p: ResolvedPrinter) =>
    p.connection === 'QZ_TRAY' && p.systemName
      ? t('printing.station.viaQz', { name: p.systemName })
      : p.printer
        ? t('printing.station.viaBrowserPrinter', { name: p.printer.name ?? '' })
        : t('printing.station.viaBrowser')

  const dialog: ReactNode = pending ? (
    <Modal
      open
      title={pending.kind === 'labels' ? t(pending.job.labels.length === 1 ? 'printing.jobs.previewLabel' : 'printing.jobs.previewLabels', { n: pending.job.labels.length }) : pending.title}
      onClose={() => setPending(null)}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={() => setPending(null)} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn" onClick={() => void download()} disabled={busy}>
            {t('printing.preview.download')}
          </button>
          <button
            type="button"
            className="btn flow"
            disabled={busy}
            onClick={() => {
              const p = pending
              setPending(null)
              void (p.kind === 'labels' ? runLabels(p) : runDoc(p))
            }}
          >
            <IconPrint /> {t('printing.preview.print')}
          </button>
        </>
      }
    >
      <p className="help">{via(pending.printer)}</p>
      {pending.kind === 'labels' ? (
        <>
          <p className="help" data-testid="print-count">
            {t('printing.jobs.summary', { labels: pending.job.labels.length, orders: pending.job.orders })}
          </p>
          <div className="prn-preview-wrap">
            <LabelPreview layout={pending.template.layout} data={pending.job.labels[0]} mediaSize={pending.printer.mediaSize} kind={pending.labelKind} />
          </div>
        </>
      ) : (
        <p>{t('printing.jobs.docReady')}</p>
      )}
    </Modal>
  ) : null

  /** «Label al guardar» por omisión: la compañía imprime directo y esta estación no se excluyó. */
  const labelOnSaveDefault = !!options.data?.printLabelsDirect && readStationConfig(tenantId).label.printOnSave

  return { printLabels, printInvoiceLabels, printDocument, dialog, busy, labelOnSaveDefault, optionsReady: !options.isPending }
}
