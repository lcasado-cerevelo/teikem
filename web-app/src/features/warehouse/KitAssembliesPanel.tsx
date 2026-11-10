// Lote 232 — pestaña «Armados» de la ficha de un kit ARMADO: explica qué es armar (salen las piezas, entran los kits; nada queda reservado),
// «se pueden armar» por almacén, el botón «Armar kits» (`inventory.adjust`) y el HISTORIAL de armados (GET …/assembled-kit/assemblies, paginado por
// el servidor; Exportar y ordenar leen todo con `exportRows`): número ARM-#####, fecha, quién, kits, cuántos quedan por desarmar, almacén y
// posición, lote del kit (y su vencimiento), piezas con su lote y posición de salida, costo (solo con `inventory.value`) y los desarmados DES-#####.
// Acción por fila «Desarmar» (`inventory.adjust`, solo si queda algo de ese armado) → DisassembleKitModal.
import { useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { DataTable, EmptyState, Panel, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconRotateCcw } from '../../kernel/ui/actionIcons'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { AssembleKitModal } from './AssembleKitModal'
import { kitAssembliesPage, useAssembledKit, useKitAssemblies } from './assembledKitApi'
import { canDisassemble, type KitAssemblyDto } from './assembledKitRules'
import { DisassembleKitModal } from './DisassembleKitModal'
import './assembledKit.css'

const A = 'warehouse.products.assembled'
const NO_ROWS: KitAssemblyDto[] = []

export interface KitAssembliesPanelProps {
  publicId: string
  sku: string
  defaultWarehousePublicId?: string | null
  defaultBinId?: number | null
}

export function KitAssembliesPanel({ publicId, sku, defaultWarehousePublicId, defaultBinId }: KitAssembliesPanelProps) {
  const t = useT()
  const f = useFormat()
  const canAdjust = useCan('inventory.adjust')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [assembling, setAssembling] = useState(false)
  const [disassembling, setDisassembling] = useState<KitAssemblyDto | null>(null)
  const kit = useAssembledKit(publicId)
  const { data, isLoading } = useKitAssemblies(publicId, { skip: (page - 1) * pageSize, take: pageSize })

  const columns = useMemo<DataColumn<KitAssemblyDto>[]>(
    () => [
      { id: 'number', header: t(`${A}.col.number`), cell: (a) => <span className="ref">{a.number}</span>, exportValue: (a) => a.number ?? '', sortValue: (a) => a.number, card: 'title' },
      { id: 'date', header: t(`${A}.col.date`), cell: (a) => (a.createdAtUtc ? f.dateTime(a.createdAtUtc) : '—'), sortValue: (a) => a.createdAtUtc },
      { id: 'by', header: t(`${A}.col.by`), cell: (a) => a.createdByName || t(`${A}.system`), sortValue: (a) => a.createdByName ?? '' },
      { id: 'quantity', header: t(`${A}.col.quantity`), align: 'end', cell: (a) => <span className="mono">{f.number(a.quantity ?? 0)}</span>, sortValue: (a) => a.quantity ?? 0 },
      { id: 'remaining', header: t(`${A}.col.remaining`), align: 'end', cell: (a) => <span className="mono">{f.number(a.remaining ?? 0)}</span>, sortValue: (a) => a.remaining ?? 0 },
      { id: 'where', header: t(`${A}.col.where`), cell: (a) => [a.warehouseCode, a.toBinCode].filter(Boolean).join(' · '), sortValue: (a) => `${a.warehouseCode ?? ''} ${a.toBinCode ?? ''}` },
      {
        id: 'lot',
        header: t(`${A}.col.lot`),
        cell: (a) => (a.kitLotNumber ? [a.kitLotNumber, a.kitLotExpiry ? t(`${A}.expires`, { date: f.date(a.kitLotExpiry) }) : null].filter(Boolean).join(' · ') : '—'),
        sortValue: (a) => a.kitLotNumber ?? '',
      },
      {
        id: 'pieces',
        header: t(`${A}.col.pieces`),
        cell: (a) => (
          <ul className="ak-lines">
            {(a.lines ?? []).map((l, i) => (
              <li key={i}>{lineText(l, t, f)}</li>
            ))}
          </ul>
        ),
        exportValue: (a) => (a.lines ?? []).map((l) => lineText(l, t, f)).join('; '),
        sortValue: (a) => (a.lines ?? []).map((l) => l.sku).join(' '),
      },
      {
        id: 'cost',
        header: t(`${A}.col.cost`),
        align: 'end',
        cell: (a) => (a.totalCost != null ? <span className="mono">{f.money(a.totalCost)}</span> : '—'),
        sortValue: (a) => a.totalCost ?? null,
      },
      {
        id: 'disassemblies',
        header: t(`${A}.col.disassemblies`),
        cell: (a) =>
          (a.disassemblies ?? []).length === 0 ? (
            '—'
          ) : (
            <ul className="ak-lines">
              {(a.disassemblies ?? []).map((d) => (
                <li key={d.publicId}>
                  <span className="ref">{d.number}</span> · {f.number(d.quantity ?? 0)}
                </li>
              ))}
            </ul>
          ),
        exportValue: (a) => (a.disassemblies ?? []).map((d) => `${d.number} (${d.quantity ?? 0})`).join('; '),
        sortValue: (a) => a.disassembledQty ?? 0,
      },
    ],
    [t, f],
  )

  const actions = useMemo<RowAction<KitAssemblyDto>[]>(
    () => [
      {
        key: 'disassemble',
        label: t(`${A}.disassemble`),
        perm: 'inventory.adjust',
        visible: canDisassemble,
        icon: <IconRotateCcw />,
        onClick: (a) => setDisassembling(a),
      },
    ],
    [t],
  )

  const avail = kit.data?.availability ?? []
  return (
    <>
      <p className="ak-explain">{t(`${A}.assembleExplain`)}</p>
      {kit.data && (
        <div className="ak-avail">
          <p>{t(`${A}.stock`, { onHand: f.number(kit.data.qtyOnHand ?? 0), available: f.number(kit.data.qtyAvailable ?? 0) })}</p>
          <p className="ak-sub">{t(`${A}.availability`)}</p>
          {avail.length === 0 ? (
            <p className="help">{t(`${A}.availabilityEmpty`)}</p>
          ) : (
            <ul>
              {avail.map((a) => (
                <li key={a.warehousePublicId}>
                  {t(`${A}.availabilityRow`, { warehouse: a.warehouseCode ?? '', n: f.number(a.canAssemble ?? 0), total: f.number(a.canAssembleTotal ?? 0) })}
                </li>
              ))}
            </ul>
          )}
          {canAdjust && (
            <button type="button" className="btn flow" onClick={() => setAssembling(true)}>
              {t(`${A}.assemble`)}
            </button>
          )}
        </div>
      )}
      <Panel flush icon={<IconLayers />} title={t(`${A}.history`)} badge={data ? (data.total ?? 0) : undefined}>
        <DataTable
          label={t(`${A}.history`)}
          columns={columns}
          rows={data?.items ?? NO_ROWS}
          rowKey={(a) => a.publicId ?? a.number ?? ''}
          loading={isLoading}
          page={page}
          pageSize={pageSize}
          total={data?.total ?? 0}
          onPage={setPage}
          onPageSize={(n) => {
            setPageSize(n)
            setPage(1)
          }}
          rowActions={actions}
          exportFileName={`${t(`${A}.history`)} ${sku}`}
          exportRows={() => fetchAllPages(kitAssembliesPage(publicId))}
          empty={<EmptyState title={t(`${A}.historyEmpty`)} />}
        />
      </Panel>
      <AssembleKitModal
        open={assembling}
        publicId={publicId}
        sku={sku}
        defaultWarehousePublicId={defaultWarehousePublicId}
        defaultBinId={defaultBinId}
        onClose={() => setAssembling(false)}
      />
      <DisassembleKitModal publicId={publicId} assembly={disassembling} onClose={() => setDisassembling(null)} />
    </>
  )
}

/** «A · lote L1 (A-01) × 4» */
function lineText(l: NonNullable<KitAssemblyDto['lines']>[number], t: ReturnType<typeof useT>, f: ReturnType<typeof useFormat>): string {
  const where = l.lotNumber ? t(`${A}.where`, { bin: l.fromBinCode ?? '', lot: l.lotNumber }) : (l.fromBinCode ?? '')
  return `${l.sku ?? ''} · ${where} × ${f.number(l.quantity ?? 0)}`
}
