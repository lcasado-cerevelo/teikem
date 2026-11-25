// Lote 245 — «Agregar por nombre» (sales.chain): el alta masiva por el INICIO del nombre del cliente. Prefijo (mínimo 3 letras), «Palabra completa» (encendido) y
// «Mover también los que están en otra cadena» (apagado) → «Simular» (no escribe nada). La tabla muestra la acción de cada cliente y una casilla para excluirlo a mano:
// al desmarcar, la fila pasa a «Excluido» SIN volver a simular (las exclusiones no entran en la huella). «Aplicar a N clientes» pide confirmación y solo está habilitado
// con una simulación vigente; si el servidor da 409 (los clientes cambiaron), se muestra el mensaje y se vuelve a simular conservando las exclusiones. Hasta 2000.
import { useMemo, useState } from 'react'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Panel, toast, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import {
  actionLabelKey,
  applyRequest,
  canApply,
  canExclude,
  chainOrNone,
  effectiveAction,
  keepExclusions,
  prefixOk,
  previewIsCurrent,
  previewRequest,
  summarize,
  type BulkAction,
  type BulkSettings,
} from './chainRules'
import { useApplyBulk, usePreviewBulk, type ChainBulkPreview, type ChainBulkRow, type ChainDetail } from './chainsApi'

const ACTION_TONE: Record<BulkAction, ChipTone> = { ASSIGN: 'cap', MOVE: 'route', ALREADY: 'neutral', SKIP_OTHER_CHAIN: 'warn', EXCLUDED: 'fail' }

export function ChainBulkAssign({ chain }: { chain: ChainDetail }) {
  const t = useT()
  const publicId = chain.publicId ?? ''
  const [prefix, setPrefix] = useState('')
  const [wholeWord, setWholeWord] = useState(true)
  const [moveOther, setMoveOther] = useState(false)
  const [preview, setPreview] = useState<ChainBulkPreview | null>(null)
  const [simulated, setSimulated] = useState<BulkSettings | null>(null)   // lo que se simuló (el servidor puede normalizar el prefijo al responder)
  const [excluded, setExcluded] = useState<ReadonlySet<string>>(new Set())
  const [message, setMessage] = useState<{ text: string; tone: 'error' | 'note' } | null>(null)
  const [confirm, setConfirm] = useState(false)
  const previewM = usePreviewBulk(publicId)
  const applyM = useApplyBulk(publicId)
  const settings = { prefix, wholeWord, moveFromOtherChains: moveOther }
  const summary = preview ? summarize(preview, excluded) : null
  const current = previewIsCurrent(simulated, settings)

  const simulate = async (keep: ReadonlySet<string>) => {
    setMessage(null)
    try {
      const p = await previewM.mutateAsync(previewRequest(settings))
      setPreview(p)
      setSimulated(settings)
      setExcluded(keepExclusions(keep, p))
      return p
    } catch (e) {
      setPreview(null)
      setSimulated(null)
      setExcluded(new Set())
      setMessage({ text: applyProblemDetails(e).title, tone: 'error' })
      return null
    }
  }

  const apply = async () => {
    if (!preview || !simulated) return
    try {
      const r = await applyM.mutateAsync(applyRequest(preview, simulated, excluded))
      toast.success(t('chains.bulk.applied', { n: (r.assigned ?? 0) + (r.moved ?? 0) }))
      setPreview(null)
      setSimulated(null)
      setExcluded(new Set())
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        // los clientes cambiaron desde la simulación: se muestra el mensaje y se vuelve a simular conservando las exclusiones
        const text = applyProblemDetails(e).title
        const p = await simulate(excluded)
        setMessage({ text: p ? `${text} ${t('chains.bulk.resimulated')}` : text, tone: 'error' })
        return
      }
      throw e
    }
  }

  const columns = useMemo<DataColumn<ChainBulkRow>[]>(
    () => [
      {
        id: 'include',
        header: t('chains.bulk.col.include'),
        sortable: false,
        exportable: false,
        cell: (r) => (
          <input
            type="checkbox"
            className="chn-check"
            aria-label={t('chains.bulk.includeRow', { name: r.name ?? '' })}
            checked={!excluded.has(r.clientPublicId ?? '') && canExclude(r)}
            disabled={!canExclude(r)}
            onChange={() =>
              setExcluded((s) => {
                const n = new Set(s)
                const id = r.clientPublicId ?? ''
                if (n.has(id)) n.delete(id)
                else n.add(id)
                return n
              })
            }
          />
        ),
      },
      { id: 'code', header: t('chains.clients.code'), cell: (r) => <span className="ref">{r.code}</span>, sortValue: (r) => r.code ?? '', exportValue: (r) => r.code ?? '' },
      { id: 'name', header: t('chains.clients.name'), cell: (r) => <b className="chn-name">{r.name}</b>, sortValue: (r) => r.name ?? '', exportValue: (r) => r.name ?? '', card: 'title' },
      { id: 'current', header: t('chains.bulk.col.current'), cell: (r) => chainOrNone(r.currentChainName, t), sortValue: (r) => r.currentChainName ?? '', exportValue: (r) => chainOrNone(r.currentChainName, t) },
      {
        id: 'action',
        header: t('chains.bulk.col.action'),
        cell: (r) => {
          const a = effectiveAction(r, excluded)
          return <Chip tone={ACTION_TONE[a]}>{t(actionLabelKey(a))}</Chip>
        },
        sortValue: (r) => effectiveAction(r, excluded),
        exportValue: (r) => t(actionLabelKey(effectiveAction(r, excluded))),
      },
    ],
    [t, excluded],
  )

  return (
    <Panel icon={<IconUsers />} title={t('chains.bulk.title')} subtitle={t('chains.bulk.subtitle')} collapsible defaultCollapsed storageKey="chains.bulk">
      <div className="chn-bulk">
        <div className="chn-bulk-form">
          <div className="f chn-bulk-prefix">
            <label htmlFor="chn-prefix">{t('chains.bulk.prefix')}</label>
            <input id="chn-prefix" value={prefix} maxLength={100} placeholder={t('chains.bulk.prefixPlaceholder')} onChange={(e) => setPrefix(e.target.value)} />
            <p className="help">{t('chains.bulk.prefixHelp')}</p>
          </div>
          <label className="sw">
            <input type="checkbox" role="switch" checked={wholeWord} onChange={(e) => setWholeWord(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('chains.bulk.wholeWord')}</span>
          </label>
          <label className="sw">
            <input type="checkbox" role="switch" checked={moveOther} onChange={(e) => setMoveOther(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('chains.bulk.moveOther')}</span>
          </label>
          <button type="button" className="btn flow" disabled={!prefixOk(prefix) || previewM.isPending} onClick={() => void simulate(excluded)}>
            {previewM.isPending ? t('common.loading') : t('chains.bulk.simulate')}
          </button>
        </div>

        {message && (
          <p className={message.tone === 'error' ? 'ferr' : 'note'} role="alert">
            {message.text}
          </p>
        )}

        {preview && summary && (
          <div className="chn-bulk-result">
            {preview.tooMany ? (
              <p className="ferr" role="alert">
                {t('chains.bulk.tooMany', { n: preview.matched ?? 0, max: preview.maxBulk ?? 0 })}
              </p>
            ) : (
              <>
                <p className="chn-bulk-counts" role="status">
                  {t('chains.bulk.counts', { matched: summary.matched, assign: summary.toAssign, move: summary.toMove, already: summary.already, skipped: summary.skippedOtherChain, excluded: summary.excluded })}
                </p>
                {!current && <p className="note">{t('chains.bulk.stale')}</p>}
                <DataTable
                  label={t('chains.bulk.title')}
                  columns={columns}
                  rows={preview.rows ?? []}
                  rowKey={(r) => r.clientPublicId ?? ''}
                  dense
                  empty={t('chains.bulk.noMatches')}
                  exportFileName={`chain-bulk-${chain.name ?? ''}`}
                />
              </>
            )}
            <div className="chn-actions">
              <button type="button" className="btn flow" disabled={!canApply(preview, simulated, settings, excluded) || applyM.isPending} onClick={() => setConfirm(true)}>
                {t('chains.bulk.apply', { n: summary.applicable })}
              </button>
            </div>
          </div>
        )}
      </div>
      <ConfirmDialog
        open={confirm}
        title={t('chains.bulk.confirmTitle')}
        message={t('chains.bulk.confirmMessage', { n: summary?.applicable ?? 0, name: chain.name ?? '', move: summary?.toMove ?? 0 })}
        confirmLabel={t('chains.bulk.apply', { n: summary?.applicable ?? 0 })}
        onClose={() => setConfirm(false)}
        onConfirm={apply}
      />
    </Panel>
  )
}
