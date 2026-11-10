// Lote 232 — piezas del KIT ARMADO dentro del editor de producto (ProductEditorModal):
// - `KitQuestion`: «¿Lo que va a crear es un kit?» (No por omisión) arriba del alta; con «Sí» aparecen la composición y «¿Cuántos kits arma ahora?».
// - `NewKitBlock`: composición (KitPiecesEditor del lote 231, sin «No sustituible»: el kit armado no tiene sustitución) con lo que la pantalla puede
//   revisar (sin kits como pieza, sin serie, mismo dueño, activas) y, con `inventory.adjust`, cuántos armar al crear (almacén y posición destino) con
//   «se pueden armar N» calculado con lo disponible de las piezas en ese almacén (GET /inventory/exit-options; el servidor revalida).
// - `EditKitBlock`: en edición, la composición (editable solo mientras el servidor no la bloquee: `compositionLocked`), los kits en existencia, «se
//   pueden armar» por almacén y el enlace «Armar y desarmar» a la ficha (`?tab=assemblies`).
// - `KitInfoField`: campo informativo (rastreo y costo los deciden las piezas).
// Ayudas (fichas de las piezas, composición en edición) en `assembledKitHooks.ts`; reglas puras en `assembledKitRules.ts`; datos en `assembledKitApi.ts`; textos en `warehouse.products.assembled.*`.
import { useEffect } from 'react'
import { useFormContext, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Field, NumberInput } from '../../kernel/ui'
import { useWarehouses } from './api'
import { usePiecesExitOptions } from './assembledKitApi'
import type { useEditKitComposition } from './assembledKitHooks'
import { KIT_EXCLUDED_ZONE_TYPES, canAssembleInOne, canAssembleTotal, pieceStocksFromExitOptions, type PieceInfo } from './assembledKitRules'
import { KitPiecesEditor } from './KitPiecesEditor'
import { pieceQuantity, type KitPiece } from './kitPieces'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import './assembledKit.css'

const A = 'warehouse.products.assembled'

/** «¿Lo que va a crear es un kit?»: radios nativos sobre el campo `kitChoice` del formulario ('no' | 'yes'). */
export function KitQuestion({ idPrefix }: { idPrefix: string }) {
  const t = useT()
  const { register } = useFormContext()
  return (
    <fieldset className="ak-question" aria-describedby={`${idPrefix}-kq-help`}>
      <legend>{t(`${A}.question`)}</legend>
      <div className="ak-question-opts">
        <label>
          <input type="radio" value="no" {...register('kitChoice')} /> {t(`${A}.no`)}
        </label>
        <label>
          <input type="radio" value="yes" {...register('kitChoice')} /> {t(`${A}.yes`)}
        </label>
      </div>
      <p id={`${idPrefix}-kq-help`} className="help">
        {t(`${A}.questionHelp`)}
      </p>
    </fieldset>
  )
}

/** Campo de solo lectura con su etiqueta (rastreo y costo de un kit armado). */
export function KitInfoField({ id, label, value, className }: { id: string; label: string; value: string; className?: string }) {
  return (
    <div className={['f', className].filter(Boolean).join(' ')}>
      <label htmlFor={id}>{label}</label>
      <input id={id} value={value} disabled readOnly />
    </div>
  )
}

interface NewKitBlockProps {
  idPrefix: string
  pieces: KitPiece[]
  setPieces: (pieces: KitPiece[]) => void
  infos: ReadonlyMap<string, PieceInfo>
  /** Problema de las piezas a la vista (tras intentar guardar). */
  problem: string | null
  canAdjust: boolean
}

/** Alta: composición del kit y «¿Cuántos kits arma ahora?». Los campos de armar van en el formulario del editor (assembleQuantity…). */
export function NewKitBlock({ idPrefix, pieces, setPieces, infos, problem, canAdjust }: NewKitBlockProps) {
  const t = useT()
  const f = useFormat()
  const { control, setValue } = useFormContext()
  const owner = useWatch({ control, name: 'ownerClientPublicId' }) as string | null
  const warehouse = useWatch({ control, name: 'assembleWarehousePublicId' }) as string | null
  const { data: warehouses } = useWarehouses({}, { handleAccessDenied: false })
  const active = (warehouses ?? []).filter((w) => w.isActive !== false)
  // con un solo almacén activo, ese (el servidor haría lo mismo)
  useEffect(() => {
    if (!warehouse && active.length === 1 && active[0].publicId) setValue('assembleWarehousePublicId', active[0].publicId)
  }, [warehouse, active, setValue])

  const ready = pieces.filter((p) => p.productPublicId && pieceQuantity(p.quantity) != null && infos.has(p.productPublicId))
  const exit = usePiecesExitOptions(
    canAdjust ? warehouse : null,
    ready.map((p) => p.productPublicId!),
    { handleAccessDenied: false },
  )
  const stocks = exit.data
    ? pieceStocksFromExitOptions(
        ready.map((p) => ({ productPublicId: p.productPublicId!, quantity: pieceQuantity(p.quantity) ?? 0, lotTracked: (infos.get(p.productPublicId!)?.trackingTypeCode ?? '') === 'LOT' })),
        exit.data.items ?? [],
      )
    : null
  const inOne = stocks ? canAssembleInOne(stocks) : null
  const total = stocks ? canAssembleTotal(stocks) : null

  return (
    <section className="pe-block ak-block" aria-labelledby={`${idPrefix}-kit`}>
      <h3 id={`${idPrefix}-kit`} className="pe-block-title">
        {t(`${A}.composition`)}
      </h3>
      <p className="help">{t(`${A}.compositionHelp`)}</p>
      <KitPiecesEditor
        idPrefix={`${idPrefix}-kp`}
        pieces={pieces}
        onChange={setPieces}
        ownOnly={!owner}
        ownerClientPublicId={owner}
        legend={t(`${A}.pieces`)}
        substitutable={false}
      />
      {problem && (
        <p className="ferr" role="alert">
          {problem}
        </p>
      )}
      <h4 className="ak-sub">{t(`${A}.assembleNow`)}</h4>
      {canAdjust ? (
        <>
          <p className="help">{t(`${A}.assembleNowHelp`)}</p>
          <div className="pe-grid">
            <Field name="assembleQuantity" label={t(`${A}.assembleQty`)}>
              <NumberInput className="mono" min={1} step={1} />
            </Field>
            <Field name="assembleWarehousePublicId" label={t(`${A}.assembleWarehouse`)}>
              <WarehousePickerInput placeholder={t('warehouse.products.fields.none')} />
            </Field>
            <Field name="assembleToBinId" label={t(`${A}.assembleBin`)} className="span2">
              <BinPickerInput warehousePublicId={warehouse} excludeZoneTypeCodes={KIT_EXCLUDED_ZONE_TYPES} placeholder={t('warehouse.products.fields.none')} />
            </Field>
          </div>
          <p className="ak-can" role="status">
            {inOne === null ? (
              t(`${A}.canAssemblePending`)
            ) : (
              <>
                <strong>{t(`${A}.canAssemble`, { n: f.number(inOne) })}</strong>
                {total !== null && total > inOne && <> {t(`${A}.canAssembleTotal`, { n: f.number(total) })}</>}
              </>
            )}
          </p>
        </>
      ) : (
        <p className="help">{t(`${A}.noAdjustPerm`)}</p>
      )}
    </section>
  )
}

interface EditKitBlockProps {
  idPrefix: string
  publicId: string
  editable: boolean
  composition: ReturnType<typeof useEditKitComposition>
  problem: string | null
  onNavigate: () => void
}

/** Edición: composición (o solo lectura con «ya no se cambia»), existencia de kits, «se pueden armar» y el acceso a armar/desarmar. */
export function EditKitBlock({ idPrefix, publicId, editable, composition, problem, onNavigate }: EditKitBlockProps) {
  const t = useT()
  const f = useFormat()
  const { dto, pieces, setPieces, locked, loading } = composition
  const { control } = useFormContext()
  const owner = useWatch({ control, name: 'ownerClientPublicId' }) as string | null
  const canChange = editable && !locked && !loading
  return (
    <section className="pe-block ak-block" aria-labelledby={`${idPrefix}-kit`}>
      <h3 id={`${idPrefix}-kit`} className="pe-block-title">
        {t(`${A}.composition`)}
      </h3>
      {loading ? (
        <p className="help">{t('common.loading')}</p>
      ) : canChange ? (
        <KitPiecesEditor idPrefix={`${idPrefix}-kp`} pieces={pieces} onChange={setPieces} ownOnly={!owner} ownerClientPublicId={owner} legend={t(`${A}.pieces`)} substitutable={false} />
      ) : (
        <>
          <KitPiecesEditor idPrefix={`${idPrefix}-kp`} pieces={pieces} readOnly legend={t(`${A}.pieces`)} substitutable={false} />
          {locked && (
            <p className="note ak-locked">
              <strong>{t(`${A}.locked`)}</strong> {t(`${A}.lockedWhy`)}
            </p>
          )}
        </>
      )}
      {problem && (
        <p className="ferr" role="alert">
          {problem}
        </p>
      )}
      {dto && (
        <div className="ak-avail">
          <p>{t(`${A}.stock`, { onHand: f.number(dto.qtyOnHand ?? 0), available: f.number(dto.qtyAvailable ?? 0) })}</p>
          <p className="ak-sub">{t(`${A}.availability`)}</p>
          {(dto.availability ?? []).length === 0 ? (
            <p className="help">{t(`${A}.availabilityEmpty`)}</p>
          ) : (
            <ul>
              {(dto.availability ?? []).map((a) => (
                <li key={a.warehousePublicId}>
                  {t(`${A}.availabilityRow`, { warehouse: a.warehouseCode ?? '', n: f.number(a.canAssemble ?? 0), total: f.number(a.canAssembleTotal ?? 0) })}
                </li>
              ))}
            </ul>
          )}
          <Link className="btn sm" to={`/warehouse/products/${publicId}?tab=assemblies`} onClick={onNavigate}>
            {t(`${A}.openAssemblies`)}
          </Link>
        </div>
      )}
    </section>
  )
}
