// Lote 13 — estado de la rejilla de líneas del detalle del recibo (lo usan `ReceiptDetailPanel`, para las notas y el botón
// Confirmar, y `ReceiptLinesEditor`, que la pinta). Las filas viven aquí (lo tecleado como texto) y se ponen al día con la
// ficha del servidor (`reconcileRows`) sin pisar lo que se está tecleando. Guardado por fila: al salir del campo, con
// Enter o al elegir producto; los guardados van en FILA ÚNICA (uno tras otro, en el orden en que se pidieron) para que el
// alta de una fila sepa cuál es su línea nueva y el estatus del recibo se sincronice en orden. La ficha que devuelve cada
// escritura queda en caché (`useSaveReceiptLine`) e invalida la lista. Lógica pura en receiptLineEdit.ts.
// Lote 16: `pickTarget` pone la posición destino de una fila (recibo directo) y la guarda de una vez (PUT con `targetBinId`
// o `clearTargetBin`; en una fila nueva va en su POST cuando esté completa).
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { useT } from '../../kernel/i18n'
import { useSaveReceiptLine, type ReceiptDetailDto } from './api'
import {
  ensureTrailingEmpty,
  linePayload,
  mergeSaved,
  newLineFrom,
  onExpectedInput,
  onProductPicked,
  onReceivedInput,
  onTargetPicked,
  reconcileRows,
  rowErrorsFromProblem,
  rowFromLine,
  rowsFromDetail,
  type LineRow,
  type RowErrors,
  type RowField,
  type RowsMode,
} from './receiptLineEdit'

type LineDto = components['schemas']['ReceiptLineDto']
type PickedProduct = { publicId?: string | null; sku?: string | null; name?: string | null; trackingTypeCode?: string | null } | null
type PickedBin = { id?: number | null; code?: string | null; zoneTypeCode?: string | null } | null

// claves locales de las filas nuevas (estables: el foco no se pierde al guardarse la fila)
let keySeq = 0
const newKey = () => `new-${++keySeq}`

export interface ReceiptLineRowsState {
  rows: LineRow[]
  mode: RowsMode
  /** Se teclea el esperado o lo recibido de una fila. */
  input: (key: string, field: 'expected' | 'received', text: string) => void
  /** Se elige el producto de una fila (sin documento): si ya hay recibido, se guarda. */
  pickProduct: (key: string, product: PickedProduct) => void
  /** Lote 16: se elige (o se quita) la posición destino de una fila: se guarda al elegir. */
  pickTarget: (key: string, bin: PickedBin) => void
  /** Guarda la fila si tiene algo que guardar (en la fila única de guardados). */
  save: (key: string) => Promise<void>
  /** Quita una fila: la nueva sin guardar, en el cliente; la guardada, con `DELETE` (lanza el error del servidor). */
  remove: (key: string) => Promise<void>
  /** La línea que devolvió otra escritura (Lote y series): la fila queda como en el servidor. */
  applyLine: (line: LineDto) => void
  /** Errores por línea que devolvió Confirmar (`lines[i]`), por id de línea. */
  setLineErrors: (byLineId: ReadonlyMap<number, string[]>) => void
}

export function useReceiptLineRows(receipt: ReceiptDetailDto, mode: RowsMode): ReceiptLineRowsState {
  const t = useT()
  const publicId = receipt.header?.publicId ?? ''
  const { mutateAsync } = useSaveReceiptLine()
  const [rows, setRowsState] = useState<LineRow[]>(() => rowsFromDetail(receipt.lines, mode, newKey))
  const rowsRef = useRef(rows)
  const modeRef = useRef(mode)
  useEffect(() => {
    modeRef.current = mode
  })
  const queue = useRef<Promise<void>>(Promise.resolve())

  const commit = useCallback((fn: (prev: LineRow[]) => LineRow[]) => {
    rowsRef.current = fn(rowsRef.current)
    setRowsState(rowsRef.current)
  }, [])
  const update = useCallback(
    (key: string, fn: (row: LineRow) => LineRow) => commit((prev) => prev.map((r) => (r.key === key ? fn(r) : r))),
    [commit],
  )
  const trailing = useCallback(
    () => commit((prev) => (modeRef.current.manual && modeRef.current.editable ? ensureTrailingEmpty(prev, newKey) : prev)),
    [commit],
  )

  // la ficha del servidor cambió (guardado, otra consulta, confirmación): se pone al día sin pisar lo tecleado
  const { manual, editable } = mode
  const direct = mode.direct === true
  useEffect(() => {
    commit((prev) => reconcileRows(prev, receipt.lines, { manual, editable }, newKey))
  }, [receipt.lines, manual, editable, commit])

  const doSave = useCallback(
    async (key: string) => {
      const row = rowsRef.current.find((r) => r.key === key)
      if (!row) return
      const payload = linePayload(row, modeRef.current.manual)
      if (payload.kind === 'none' || payload.kind === 'incomplete') return
      if (payload.kind === 'invalid') {
        const errors: RowErrors = {}
        for (const [field, issue] of Object.entries(payload.issues)) {
          if (issue) errors[field as RowField] = t(issue.key, issue.params)
        }
        update(key, (r) => ({ ...r, errors }))
        return
      }
      const known = new Set(rowsRef.current.flatMap((r) => (r.lineId !== null ? [r.lineId] : [])))
      update(key, (r) => ({ ...r, saving: true }))
      try {
        const dto =
          payload.kind === 'add'
            ? await mutateAsync({ publicId, action: 'add', body: payload.body })
            : await mutateAsync({ publicId, action: 'update', lineId: payload.lineId, body: payload.body })
        const line = payload.kind === 'add' ? newLineFrom(dto.lines, known) : (dto.lines ?? []).find((l) => l.id === payload.lineId)
        update(key, (cur) => (line ? mergeSaved(cur, row, line) : { ...cur, saving: false }))
        trailing()
      } catch (err) {
        const problem = applyProblemDetails(err)
        update(key, (cur) => ({ ...cur, saving: false, errors: rowErrorsFromProblem(problem) }))
      }
    },
    [mutateAsync, publicId, t, trailing, update],
  )

  const save = useCallback(
    (key: string) => {
      const next = queue.current.then(() => doSave(key))
      queue.current = next.catch(() => undefined)
      return next
    },
    [doSave],
  )

  const input = useCallback(
    (key: string, field: 'expected' | 'received', text: string) => {
      update(key, (r) => (field === 'received' ? onReceivedInput(r, text, modeRef.current.manual) : onExpectedInput(r, text)))
      trailing()
    },
    [trailing, update],
  )

  const pickProduct = useCallback(
    (key: string, product: PickedProduct) => {
      update(key, (r) => onProductPicked(r, product))
      trailing()
      const row = rowsRef.current.find((r) => r.key === key)
      if (product && row && (row.lineId !== null || row.received.trim() !== '')) void save(key)
    },
    [save, trailing, update],
  )

  const pickTarget = useCallback(
    (key: string, bin: PickedBin) => {
      update(key, (r) => onTargetPicked(r, bin))
      void save(key)
    },
    [save, update],
  )

  const remove = useCallback(
    async (key: string) => {
      const row = rowsRef.current.find((r) => r.key === key)
      if (!row) return
      if (row.lineId === null) {
        commit((prev) => prev.filter((r) => r.key !== key))
        trailing()
        return
      }
      const lineId = row.lineId
      // en la fila única: no se quita una línea mientras otra escritura del recibo va en camino
      const next = queue.current.then(async () => {
        await mutateAsync({ publicId, action: 'remove', lineId })
        commit((prev) => prev.filter((r) => r.key !== key))
        trailing()
      })
      queue.current = next.catch(() => undefined)
      return next
    },
    [commit, mutateAsync, publicId, trailing],
  )

  const applyLine = useCallback(
    (line: LineDto) => commit((prev) => prev.map((r) => (r.lineId === line.id ? rowFromLine(line, r.key) : r))),
    [commit],
  )

  const setLineErrors = useCallback(
    (byLineId: ReadonlyMap<number, string[]>) =>
      commit((prev) =>
        prev.map((r) => {
          const msgs = r.lineId !== null ? byLineId.get(r.lineId) : undefined
          return msgs && msgs.length > 0 ? { ...r, errors: { ...r.errors, row: msgs.join(' ') } } : r
        }),
      ),
    [commit],
  )

  return useMemo(
    () => ({ rows, mode: { manual, editable, direct }, input, pickProduct, pickTarget, save, remove, applyLine, setLineErrors }),
    [rows, manual, editable, direct, input, pickProduct, pickTarget, save, remove, applyLine, setLineErrors],
  )
}
