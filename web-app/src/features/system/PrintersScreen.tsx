// Sistema → Impresoras y labels (Lote F-G1; maqueta `printers()`): «cada estación guarda su propia configuración» → los dos paneles de
// ESTA estación (labels y documentos), el constructor del label con su vista previa y, debajo (no están en la maqueta pero hacen
// falta), el ajuste de la compañía «Imprimir labels directo a la impresora» y la tabla de impresoras de la compañía.
// Arriba, «Esta computadora»: el estado de QZ Tray (impresión directa) y «Detectar impresoras de esta computadora».
// Permiso printers.manage, módulo SYSTEM (la ruta lo exige).
import { useCallback, useEffect, useState } from 'react'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, Panel, Spinner, toast } from '../../kernel/ui'
import { IconGear, IconPrint } from '../../kernel/ui/screenIcons'
import { getQzClient, QZ_DOWNLOAD_URL, type QzState } from '../../kernel/ui/qzTray'
import { LabelBuilderPanel } from '../printing/LabelBuilderPanel'
import { PrintersPanel } from '../printing/PrintersPanel'
import { useStationOptions } from '../printing/printingApi'
import { StationPrintPanel } from '../printing/StationPrintPanel'
import { readStationConfig, resolvePrinter, writeStationConfig, type StationConfig } from '../printing/stationConfig'
import '../printing/printing.css'

function QzPanel({ detected, onDetected, signing }: { detected: readonly string[]; onDetected: (list: string[]) => void; signing: boolean | undefined }) {
  const t = useT()
  const [state, setState] = useState<QzState | 'checking'>('checking')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let alive = true
    void getQzClient()
      .connect()
      .then((s) => alive && setState(s))
      .catch(() => alive && setState('notRunning'))
    return () => {
      alive = false
    }
  }, [])

  const detect = async () => {
    setBusy(true)
    try {
      const qz = getQzClient()
      const s = await qz.connect()
      setState(s)
      if (s !== 'connected') {
        toast.info(t(`printing.qz.${s}`))
        return
      }
      const list = await qz.findPrinters()
      onDetected(list)
      toast.success(t('printing.qz.found', { n: list.length }))
    } catch (e) {
      toast.error(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Panel icon={<IconPrint />} title={t('printing.qz.title')}>
      <div className="prn-qz">
        <span role="status" aria-live="polite">
          {state === 'checking' ? (
            <Spinner label={t('printing.qz.checking')} />
          ) : state === 'connected' ? (
            <Chip tone="deliv">{t('printing.qz.connected')}</Chip>
          ) : (
            <Chip tone="warn">{t('printing.qz.notDetected')}</Chip>
          )}
        </span>
        <button type="button" className="btn" disabled={busy} onClick={() => void detect()}>
          {busy ? t('common.loading') : t('printing.qz.detect')}
        </button>
        {state !== 'connected' && state !== 'checking' && (
          <a href={QZ_DOWNLOAD_URL} target="_blank" rel="noreferrer noopener">
            {t('printing.qz.install')}
          </a>
        )}
      </div>
      <p className="help">
        {state === 'connected' ? t('printing.qz.connectedHelp') : state === 'checking' ? '' : t(`printing.qz.${state}`)}
        {state === 'connected' && signing === false ? ` ${t('printing.qz.unsigned')}` : ''}
      </p>
      {detected.length > 0 && (
        <p className="help" data-testid="qz-detected">
          {t('printing.qz.list', { list: detected.join(' · ') })}
        </p>
      )}
    </Panel>
  )
}

export default function PrintersScreen() {
  const t = useT()
  const { tenantId, me } = useSession()
  const company = me?.tenantName ?? ''
  const options = useStationOptions()
  const [saved, setSaved] = useState<StationConfig>(() => readStationConfig(tenantId))
  const [detected, setDetected] = useState<string[]>([])

  const onSave = useCallback(
    (next: StationConfig, part: 'label' | 'doc') => {
      setSaved((prev) => {
        const merged = part === 'label' ? { ...prev, label: next.label } : { ...prev, doc: next.doc }
        if (!writeStationConfig(tenantId, merged)) toast.error(t('printing.station.storageFailed'))
        return merged
      })
    },
    [tenantId, t],
  )
  const labelPrinter = resolvePrinter('label', saved, options.data)

  return (
    <div className="prn-screen">
      <div className="head">
        <div>
          <h1>{t('printing.title')}</h1>
          <p>{t('printing.subtitle')}</p>
        </div>
      </div>
      <div className="note prn-note">
        <IconGear /> {t('printing.perStationNote')}
      </div>
      {options.isError && (
        <p className="ferr" role="alert">
          {applyProblemDetails(options.error).title}
        </p>
      )}
      <QzPanel detected={detected} onDetected={setDetected} signing={options.data?.qzSigningConfigured} />
      <div className="prn-cols">
        <StationPrintPanel kind="label" options={options.data} saved={saved} onSave={(c) => onSave(c, 'label')} detected={detected} company={company} />
        <StationPrintPanel kind="doc" options={options.data} saved={saved} onSave={(c) => onSave(c, 'doc')} detected={detected} company={company} />
      </div>
      <LabelBuilderPanel mediaSize={labelPrinter.mediaSize} company={company} />
      <LabelBuilderPanel kind="INVOICE" mediaSize={labelPrinter.mediaSize} company={company} />
      
      <PrintersPanel detected={detected} />
    </div>
  )
}
