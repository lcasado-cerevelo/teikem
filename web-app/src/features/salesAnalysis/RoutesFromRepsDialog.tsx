// Lote 244 — «Pasar rutas desde representantes»: primero SIMULA (no escribe nada) y muestra qué haría; después, con confirmación, aplica.
// La misma regla que la migración: los clientes sin ruta reciben la de su representante y cada ruta sin titular queda con el usuario del
// representante (titular si es uno; apoyos si son varios).
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { useT } from '../../kernel/i18n/useT'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { Modal } from '../../kernel/ui/Modal'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useRoutesFromReps } from './salesAnalysisApi'
import './saRouteSetup.css'

type Report = components['schemas']['SalesRoutesFromRepsDto']
type Alert = components['schemas']['SalesRepAlertDto']

function AlertList({ title, rows }: { title: string; rows: Alert[] }) {
  if (rows.length === 0) return null
  return (
    <div>
      <strong>{title} ({rows.length})</strong>
      <ul>
        {rows.map((r) => (
          <li key={`${r.repId}-${r.routeId}`}>{r.repName}{r.routeName ? ` · ${r.routeName}` : ''}</li>
        ))}
      </ul>
    </div>
  )
}

export function RoutesFromRepsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const run = useRoutesFromReps()
  const [report, setReport] = useState<Report | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const simulate = async () => {
    setError(null)
    setReport(null)
    try {
      setReport(await run.mutateAsync(true))
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }
  useEffect(() => {
    if (open) void simulate()
    else {
      setReport(null)
      setError(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])
  const nothing = report && (report.clientsRouted ?? 0) === 0 && (report.primariesSet ?? 0) === 0 && (report.supportsSet ?? 0) === 0
  return (
    <>
      <Modal
        open={open}
        title={t('salesAnalysis.routes.fromReps.title')}
        onClose={onClose}
        size="md"
        footer={
          <>
            <button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>
            <button type="button" className="btn" onClick={() => void simulate()} disabled={run.isPending}>{t('salesAnalysis.routes.fromReps.simulate')}</button>
            <button type="button" className="btn flow" onClick={() => setConfirming(true)} disabled={!report || !!nothing || run.isPending}>{t('salesAnalysis.routes.fromReps.apply')}</button>
          </>
        }
      >
        <div className="sa-route-results">
          <p className="sa-muted">{t('salesAnalysis.routes.fromReps.help')}</p>
          {error && <p className="ferr" role="alert">{error}</p>}
          {!report && !error && <Spinner block label={t('common.loading')} />}
          {report && (
            <>
              <p className="sa-route-note" role="status">{t('salesAnalysis.routes.fromReps.dryRunNote')}</p>
              <div className="sa-route-kpis">
                <span>{t('salesAnalysis.routes.fromReps.clientsRouted', { n: report.clientsRouted ?? 0 })}</span>
                <span>{t('salesAnalysis.routes.fromReps.primariesSet', { n: report.primariesSet ?? 0 })}</span>
                <span>{t('salesAnalysis.routes.fromReps.supportsSet', { n: report.supportsSet ?? 0 })}</span>
              </div>
              <AlertList title={t('salesAnalysis.routes.fromReps.repsWithoutUser')} rows={report.repsWithoutUser ?? []} />
              <AlertList title={t('salesAnalysis.routes.fromReps.severalReps')} rows={report.routesWithSeveralReps ?? []} />
            </>
          )}
        </div>
      </Modal>
      <ConfirmDialog
        open={confirming}
        title={t('salesAnalysis.routes.fromReps.confirmTitle')}
        message={t('salesAnalysis.routes.fromReps.confirmMsg')}
        confirmLabel={t('salesAnalysis.routes.fromReps.apply')}
        onConfirm={async () => {
          await run.mutateAsync(false)
          toast.success(t('salesAnalysis.routes.fromReps.applied'))
          onClose()
        }}
        onClose={() => setConfirming(false)}
      />
    </>
  )
}
