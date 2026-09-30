// Pulso del día (pantalla de inicio `/`). Lote F8a (P2): por paneles. `GET /api/v1/analytics/pulse` dice qué paneles ve el
// usuario (cada uno con su permiso `pulse.*`, sus permisos de datos y su módulo), en qué orden y cuáles oculta; la pantalla
// pinta exactamente esos, con el registro `pulsePanels.tsx` (claves desconocidas y ocultos se omiten). Cabecera a la
// maqueta: la fecha del día como título, el saludo, "Organizar mi Pulso" (siempre) y "Organizar el de la compañía"
// (`canOrganizeCompany`), y bajo el título si el usuario ve su Pulso personal (con "Volver al de la compañía") o el de la
// compañía. El modo Organizar (`PulseOrganizer`) reemplaza las secciones mientras está abierto.
// Historia: P3 (indicadores y gráficos con "Rango"), F6 (panel Almacén), F7A (filtro del panel Almacén y Actividad reciente).
// Lote 15 (P6, D6/D7): filas fijas SOLO aquí (clase propia `.pulse-home`; `.wrap.pulse` la usan también Indicadores y
// Gráficos). La cabecera se parte en dos: la fila de la fecha con "Organizar" (`.pulse-pin-head`, fija) y el saludo con el
// chip (`.pulse-greet`, se desplaza). La primera sección pintada queda fija justo debajo si su entrada es `pinnable` (la
// franja "Almacén hoy") y no se está organizando (`data-pinned`, `pinnedPanelKey`); si se oculta o se baja, solo queda la
// fecha. Los altos se miden (`useElementHeight`) y van en `--pulse-head-h` (desplazamiento de la franja) y `--pulse-pin-h`
// (`scroll-margin-top` de lo que se desplaza, para que el foco no quede tapado). Celular: franja compacta 2×2; acostado
// (alto bajo) solo la fecha (pulse.css).
import { useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useSession } from '../../app/session'
import { ModuleKeys, useCan, useModule } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { EmptyState } from '../../kernel/ui/EmptyState'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useElementHeight } from '../../kernel/ui/useElementWidth'
import { usePulse, usePulseCompany, useResetMyLayout } from './api'
import { pulseDateTitle } from './format'
import { PulseOrganizer } from './PulseOrganizer'
import { pinnedPanelKey, PULSE_PANELS, type PulsePanelContext } from './pulsePanels'
import { isKnownPanel, shownItems, shownPanels, type PulseScope } from './pulseLayout'
import { INDICATORS_ROUTE } from './PulseSections'
import './pulse.css'

export default function Pulse() {
  const t = useT()
  const lang = useLang()
  const { me } = useSession()
  const { data, isLoading, error } = usePulse()
  const reset = useResetMyLayout()
  const [organizing, setOrganizing] = useState<PulseScope | null>(null)
  // "Organizar el de la compañía" parte del Pulso de LA COMPAÑÍA, no del personal de quien lo abre (que puede tener
  // su propio orden/ocultos): sin esto, "Listo" reescribiría la compañía con el estado personal de quien organiza.
  const companyPulse = usePulseCompany(organizing === 'company')
  const [confirmReset, setConfirmReset] = useState(false)
  const canOpenIndicators = useCan('analytics.view')
  const analyticsOn = useModule(ModuleKeys.Analytics)
  const name = me?.fullName ?? ''

  const ctx = useMemo<PulsePanelContext | null>(
    () => (data ? { pulse: data, indicators: shownItems(data.indicators), charts: shownItems(data.charts) } : null),
    [data],
  )
  // Paneles que el usuario tiene (aunque estén ocultos): sin ninguno, bienvenida y nada que organizar.
  const hasPanels = (data?.panels ?? []).some((p) => isKnownPanel(p.key))
  const sections = ctx ? shownPanels(data?.panels).filter((p) => PULSE_PANELS[p.key].hasContent(ctx)) : []

  const pinned = pinnedPanelKey(sections, organizing != null || !data || !hasPanels)
  const [headEl, setHeadEl] = useState<HTMLDivElement | null>(null)
  const [bandEl, setBandEl] = useState<HTMLDivElement | null>(null)
  const headH = useElementHeight(headEl)
  const bandH = useElementHeight(pinned ? bandEl : null)
  const pinVars = { '--pulse-head-h': `${headH}px`, '--pulse-pin-h': `${headH + bandH}px` } as CSSProperties

  const showControls = Boolean(data && hasPanels && !organizing)
  const head = (
    <>
      {/* fila fija (D6): la fecha y los botones de organizar; sin controles (organizando) no se fija: la .orgbar ya lo es */}
      <div className={showControls ? 'head pulse-head pulse-pin-head pinned' : 'head pulse-head pulse-pin-head'} ref={setHeadEl}>
        <h1>{pulseDateTitle(new Date(), lang)}</h1>
        {showControls && data && (
          <div className="act">
            <button type="button" className="btn" onClick={() => setOrganizing('mine')}>
              {t('analytics.pulse.organizeMine')}
            </button>
            {data.canOrganizeCompany && (
              <button type="button" className="btn" onClick={() => setOrganizing('company')}>
                {t('analytics.pulse.organizeCompany')}
              </button>
            )}
          </div>
        )}
      </div>
      <div className="pulse-greet">
        <p>{t('analytics.pulse.subtitle', { name })}</p>
        {showControls && data && (
          <div className="pulse-mode">
            {data.hasPersonalLayout ? (
              <>
                <Chip tone="route">{t('analytics.pulse.personal')}</Chip>
                <span aria-hidden="true">·</span>
                <button type="button" className="pulse-link" onClick={() => setConfirmReset(true)}>
                  {t('analytics.pulse.backToCompany')}
                </button>
              </>
            ) : (
              <Chip tone="cap">{t('analytics.pulse.company')}</Chip>
            )}
          </div>
        )}
      </div>
    </>
  )

  let body: ReactNode
  if (isLoading) body = <Spinner block label={t('common.loading')} />
  else if (error || !data || !ctx) body = <EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />
  else if (organizing === 'company' && (companyPulse.isLoading || !companyPulse.data))
    body = companyPulse.error ? (
      <EmptyState title={companyPulse.error instanceof ApiError ? companyPulse.error.title : t('errors.generic')} />
    ) : (
      <Spinner block label={t('common.loading')} />
    )
  else if (organizing)
    body = <PulseOrganizer key={organizing} scope={organizing} pulse={organizing === 'company' ? companyPulse.data! : data} onClose={() => setOrganizing(null)} />
  else if (!hasPanels) body = <EmptyState title={t('analytics.pulse.welcomeTitle', { name })} body={t('analytics.pulse.welcomeBody')} />
  else if (sections.length === 0)
    // Paneles, pero ninguno con algo que mostrar (sin indicadores ni gráficos visibles, sin Almacén ni Actividad).
    body = (
      <EmptyState
        title={t('analytics.pulse.noItemsTitle')}
        body={t('analytics.pulse.noItemsBody')}
        action={
          canOpenIndicators && analyticsOn ? (
            <Link className="btn flow" to={INDICATORS_ROUTE}>
              {t('analytics.pulse.noItemsLink')}
            </Link>
          ) : undefined
        }
      />
    )
  else
    body = sections.map((p) => (
      <div
        key={p.key}
        className="pulse-sec"
        data-panel={p.key}
        data-pinned={p.key === pinned ? '' : undefined}
        ref={p.key === pinned ? setBandEl : undefined}
      >
        {PULSE_PANELS[p.key].render(ctx)}
      </div>
    ))

  return (
    <div className="wrap pulse pulse-home" style={pinVars}>
      {head}
      {body}
      <ConfirmDialog
        open={confirmReset}
        title={t('analytics.pulse.resetTitle')}
        message={t('analytics.pulse.resetBody')}
        confirmLabel={t('analytics.pulse.backToCompany')}
        onConfirm={async () => {
          await reset.mutateAsync()
          toast.success(t('analytics.pulse.resetDone'))
        }}
        onClose={() => setConfirmReset(false)}
      />
    </div>
  )
}
