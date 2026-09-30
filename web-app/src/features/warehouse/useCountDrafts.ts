// Lote 14 (P8) — captura de lo contado EN LA FILA del conteo elegido (D9; Cambios.pdf p. 14: "el campo de contado abierto
// para poder ser cambiado"). Lo tecleado vive aquí como texto por línea (`drafts`) hasta guardarse; se guarda al salir del
// campo o con Enter (`save`), y el escáner/modal de cantidad guarda directo (`capture`). Todas las escrituras van en FILA
// ÚNICA con el rowVersion más reciente de la ficha (el que devolvió la escritura anterior), así dos capturas seguidas no
// chocan entre sí; la ficha devuelta queda en caché (`useCycleCountAction`). Errores del servidor por línea, tal cual.
// `flush` guarda todo lo pendiente antes de "Confirmar conteo y ajustar".
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { useCycleCountAction, warehouseKeys, type CycleCountDetailDto, type CycleCountLineDto } from './api'
import { parseCounted } from './countView'
import { countLineIssues } from './lineRules'
import { problemText } from './problemText'

export interface CountCaptureBody {
  /** NONE/LOT: la cantidad (null = borrar la captura). */
  countedQty: number | null
  /** SERIAL: las series (vacío = "no se encontró ninguna"; null = borrar la captura). */
  serialNumbers: string[] | null
}

export interface CountDraftsState {
  drafts: ReadonlyMap<number, string>
  errors: ReadonlyMap<number, string>
  saving: ReadonlySet<number>
  /** Se teclea la cantidad de una línea. */
  input: (lineId: number, text: string) => void
  /** Guarda lo tecleado en una línea (si cambió). */
  save: (lineId: number) => Promise<void>
  /** Guarda una captura directa (modal de cantidad o de series); lanza el error del servidor. */
  capture: (lineId: number, body: CountCaptureBody) => Promise<CycleCountDetailDto | null>
  /** Guarda todo lo tecleado; true si no quedó ningún error. */
  flush: () => Promise<boolean>
  /** rowVersion vigente (para Confirmar). */
  rowVersion: () => string | null
}

export function useCountDrafts(detail: CycleCountDetailDto): CountDraftsState {
  const t = useT()
  const qc = useQueryClient()
  const { mutateAsync } = useCycleCountAction()
  const id = detail.count?.id ?? 0
  const [drafts, setDrafts] = useState<ReadonlyMap<number, string>>(() => new Map())
  const [errors, setErrors] = useState<ReadonlyMap<number, string>>(() => new Map())
  const [saving, setSaving] = useState<ReadonlySet<number>>(() => new Set())
  const draftsRef = useRef(drafts)
  const linesRef = useRef<readonly CycleCountLineDto[]>(detail.lines ?? [])
  const rowVersionRef = useRef<string | null>(detail.rowVersion ?? null)
  const queue = useRef<Promise<unknown>>(Promise.resolve())

  // la ficha del servidor cambió (otra consulta, otra escritura): rowVersion y líneas al día
  useEffect(() => {
    rowVersionRef.current = detail.rowVersion ?? null
    linesRef.current = detail.lines ?? []
  }, [detail.rowVersion, detail.lines])

  const setDraftMap = useCallback((fn: (prev: Map<number, string>) => void) => {
    const next = new Map(draftsRef.current)
    fn(next)
    draftsRef.current = next
    setDrafts(next)
  }, [])
  const setError = useCallback((lineId: number, message: string | null) => {
    setErrors((prev) => {
      if (message === null && !prev.has(lineId)) return prev
      const next = new Map(prev)
      if (message === null) next.delete(lineId)
      else next.set(lineId, message)
      return next
    })
  }, [])
  const setSavingLine = useCallback((lineId: number, on: boolean) => {
    setSaving((prev) => {
      const next = new Set(prev)
      if (on) next.add(lineId)
      else next.delete(lineId)
      return next
    })
  }, [])

  /** Escritura en la fila única con el rowVersion vigente; deja el nuevo en el ref. */
  const enqueue = useCallback(
    (lineId: number, body: CountCaptureBody): Promise<CycleCountDetailDto | null> => {
      const run = queue.current.then(async () => {
        setSavingLine(lineId, true)
        try {
          const dto = await mutateAsync({
            id,
            action: 'capture',
            body: { rowVersion: rowVersionRef.current, lines: [{ lineId, countedQty: body.countedQty, serialNumbers: body.serialNumbers }] },
          })
          if (dto) {
            rowVersionRef.current = dto.rowVersion ?? rowVersionRef.current
            linesRef.current = dto.lines ?? linesRef.current
          }
          return dto
        } catch (err) {
          // otro usuario cambió el conteo: se recarga la ficha para seguir con su rowVersion
          if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.cycleCount })
          throw err
        } finally {
          setSavingLine(lineId, false)
        }
      })
      queue.current = run.catch(() => undefined)
      return run
    },
    [id, mutateAsync, qc, setSavingLine],
  )

  const input = useCallback(
    (lineId: number, text: string) => {
      setDraftMap((m) => m.set(lineId, text))
      setError(lineId, null)
    },
    [setDraftMap, setError],
  )

  const save = useCallback(
    async (lineId: number) => {
      const text = draftsRef.current.get(lineId)
      if (text === undefined) return
      const line = linesRef.current.find((l) => l.id === lineId)
      if (!line) return
      const qty = parseCounted(text)
      if (qty !== null && Number.isNaN(qty)) {
        setError(lineId, t('warehouse.receipts.lines.invalidNumber'))
        return
      }
      // sin cambio respecto de lo guardado: no se escribe
      if (qty === (line.countedQty ?? null)) {
        setDraftMap((m) => m.delete(lineId))
        return
      }
      const issue = countLineIssues({ sku: line.sku ?? '', trackingTypeCode: line.trackingTypeCode, countedQty: qty, serials: [] })[0]
      if (issue) {
        setError(lineId, t(`warehouse.lineRules.${issue.code}`, issue.params))
        return
      }
      try {
        await enqueue(lineId, { countedQty: qty, serialNumbers: null })
        // si mientras guardaba se tecleó otra cosa, lo nuevo se queda como borrador
        if (draftsRef.current.get(lineId) === text) setDraftMap((m) => m.delete(lineId))
        setError(lineId, null)
      } catch (err) {
        setError(lineId, problemText(err))
      }
    },
    [enqueue, setDraftMap, setError, t],
  )

  const capture = useCallback(
    async (lineId: number, body: CountCaptureBody) => {
      const dto = await enqueue(lineId, body)
      setDraftMap((m) => m.delete(lineId))
      setError(lineId, null)
      return dto
    },
    [enqueue, setDraftMap, setError],
  )

  const flush = useCallback(async () => {
    const ids = [...draftsRef.current.keys()]
    for (const lineId of ids) await save(lineId)
    await queue.current
    return draftsRef.current.size === 0
  }, [save])

  const rowVersion = useCallback(() => rowVersionRef.current, [])

  return useMemo(
    () => ({ drafts, errors, saving, input, save, capture, flush, rowVersion }),
    [drafts, errors, saving, input, save, capture, flush, rowVersion],
  )
}
