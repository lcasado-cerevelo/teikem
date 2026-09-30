// Pieza "Recolección y empaque" (Lote F6; Lote 13: dos paneles) — `/warehouse/pick-batches` (maqueta `picking()`).
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta). Pestaña Recolecciones (primera, sin parámetro): con warehouse.pick,
// dos paneles lado a lado en un `SplitPane` (60/40, barra arrastrable que recuerda su posición en
// `teikem.split.pick-batches`; bajo 900 px uno debajo del otro): izquierda "Recolección" (`CollectPanel`: rejilla de líneas y
// "Recolectar (bajar de inventario)", un solo POST) y derecha "Recolecciones" (`PickBatchesPanel`: filtros, tabla, Empacar y
// Eliminar por fila, clic = ficha en un modal). Sin warehouse.pick no hay panel izquierdo: la lista ocupa todo el ancho.
// Una recolección admite productos de un solo dueño; sin posición ni lote el servidor elige por FEFO. Manual 06 §7.
// Pestaña 'Reabasto' (?tab=replenish): cola de tareas REPLENISH (iniciar/completar con warehouse.pick, asignar/cancelar con
// warehouse.manage) y 'Correr reabasto' (warehouse.pick); ver taskQueue.tsx.
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { SplitPane, Tabs } from '../../kernel/ui'
import { IconBasket } from '../../kernel/ui/screenIcons'
import { CollectPanel } from './CollectPanel'
import { PickBatchesPanel } from './PickBatchesPanel'
import { ReplenishButton, TaskQueue } from './taskQueue'
import './warehouse.css'

const TAB_KEYS = ['batches', 'replenish'] as const
type TabKey = (typeof TAB_KEYS)[number]
const isTabKey = (v: string | null): v is TabKey => (TAB_KEYS as readonly string[]).includes(v ?? '')
const REPLENISH_TYPES = ['REPLENISH'] as const

function BatchesTab() {
  const t = useT()
  const canPick = useCan('warehouse.pick')
  const [highlight, setHighlight] = useState<string | null>(null)
  if (!canPick) return <PickBatchesPanel />
  return (
    <SplitPane storageKey="pick-batches" label={t('warehouse.pickBatches.splitLabel')} className="collect-split">
      <CollectPanel onCollected={(b) => setHighlight(b.publicId ?? null)} />
      <PickBatchesPanel highlight={highlight} />
    </SplitPane>
  )
}

export default function PickBatchListScreen() {
  const t = useT()
  // La pestaña va en la URL (?tab=replenish) para poder enlazarla (Actividad reciente).
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const tab: TabKey = isTabKey(raw) ? raw : 'batches'
  const setTab = (key: TabKey) => setParams(key === 'batches' ? {} : { tab: key }, { replace: true })

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.pickBatches.title')}</h1>
          <p>{t('warehouse.pickBatches.subtitle')}</p>
        </div>
        {tab === 'replenish' && (
          <div className="act">
            <ReplenishButton />
          </div>
        )}
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.pickBatches.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'batches', label: t('warehouse.pickBatches.tabBatches') },
            { key: 'replenish', label: t('warehouse.pickBatches.tabReplenish') },
          ]}
        />
      </div>

      {tab === 'batches' && <BatchesTab />}
      {tab === 'replenish' && <TaskQueue types={REPLENISH_TYPES} title={t('warehouse.pickBatches.replenishTitle')} icon={<IconBasket />} />}
    </div>
  )
}
