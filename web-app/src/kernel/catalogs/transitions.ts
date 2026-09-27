// Transiciones que el servidor aceptaría desde el estatus actual: réplica de StatusService.EnsureTransitionAllowedAsync.
// Solo sirve para OFRECER acciones; el servidor vuelve a validar y responde 422 status_rule si algo cambió.
import { StageKinds, sameCode, type StatusLateralEntryDto, type StatusOption } from './types'

export interface TransitionInput {
  /** Etapas del dominio (pueden incluir deshabilitadas; solo las habilitadas son destino). */
  statuses: readonly StatusOption[]
  /** Estatus actual del registro; null/'' = registro nuevo (solo etapas iniciales). */
  currentCode: string | null | undefined
  /** Reglas de entrada lateral (`/status/lateral-entries/{entityType}`). Sin reglas para un lateral = permitido. */
  lateralEntries?: readonly StatusLateralEntryDto[]
  /** Códigos destino del historial del registro, del más antiguo al último (para volver de un lateral). */
  historyToCodes?: readonly (string | null | undefined)[]
}

/** Sin reglas para ese lateral = permitido; si el tenant tiene reglas propias para él, solo cuentan esas. */
export function lateralEntryAllowed(
  entries: readonly StatusLateralEntryDto[],
  lateralCode: string,
  fromCode: string,
): boolean {
  const rules = entries.filter((r) => sameCode(r.lateralStatusCode, lateralCode))
  if (rules.length === 0) return true
  const tenantRules = rules.filter((r) => r.isTenantRule)
  const effective = tenantRules.length > 0 ? tenantRules : rules
  return effective.some((r) => sameCode(r.fromStatusCode, fromCode) && r.isAllowed === true)
}

/** Destinos válidos desde el estatus actual, en el orden del pipeline. */
export function allowedTransitions(input: TransitionInput): StatusOption[] {
  const all = [...input.statuses].sort((a, b) => a.sortOrder - b.sortOrder)
  const enabled = all.filter((s) => s.isEnabled)
  const entries = input.lateralEntries ?? []

  if (!input.currentCode) return enabled.filter((s) => s.isInitial)
  const from = all.find((s) => sameCode(s.code, input.currentCode))
  if (!from || from.stageKind === StageKinds.Terminal) return []

  const pipeline = enabled.filter((s) => s.stageKind === StageKinds.Pipeline)

  if (from.stageKind === StageKinds.Pipeline) {
    // Siguiente etapa habilitada por orden (pipeline o terminal de cierre); laterales y terminales fuera de orden por regla.
    const next = enabled.find((s) => s.sortOrder > from.sortOrder && s.stageKind !== StageKinds.Lateral)
    return enabled.filter((to) => {
      if (to.code === from.code) return false
      if (next && to.code === next.code) return true
      if (to.stageKind === StageKinds.Pipeline) return false
      return lateralEntryAllowed(entries, to.code, from.code)
    })
  }

  // Desde un lateral: volver a la última etapa del pipeline (o la siguiente), o a la inicial si nunca pasó por el pipeline.
  const pipelineCodes = new Set(all.filter((s) => s.stageKind === StageKinds.Pipeline).map((s) => s.code.toLowerCase()))
  const lastCode = [...(input.historyToCodes ?? [])].reverse().find((c) => !!c && pipelineCodes.has(c.toLowerCase()))
  const last = pipeline.find((p) => sameCode(p.code, lastCode))
  const nextOfLast = last ? pipeline.find((p) => p.sortOrder > last.sortOrder) : undefined
  return enabled.filter((to) => {
    if (to.code === from.code) return false
    if (to.stageKind === StageKinds.Pipeline) {
      return to.code === last?.code || to.code === nextOfLast?.code || (!last && to.isInitial)
    }
    return lateralEntryAllowed(entries, to.code, from.code)
  })
}

/** Posición visual de cada etapa del pipeline respecto del estatus actual. */
export type StepState = 'done' | 'current' | 'upcoming'

/**
 * Estado de cada etapa del pipeline. Si el registro está en un lateral o en un terminal, la referencia es la última
 * etapa del pipeline de su historial: esa y las anteriores quedan hechas.
 */
export function stepStates(
  pipelineSteps: readonly StatusOption[],
  current: StatusOption | undefined,
  historyToCodes: readonly (string | null | undefined)[] = [],
): Map<string, StepState> {
  const states = new Map<string, StepState>()
  const onPipeline = current?.stageKind === StageKinds.Pipeline
  let anchor: StatusOption | undefined = onPipeline ? current : undefined
  if (current && !onPipeline) {
    const lastCode = [...historyToCodes].reverse().find((c) => pipelineSteps.some((s) => sameCode(s.code, c)))
    anchor = pipelineSteps.find((s) => sameCode(s.code, lastCode))
  }
  for (const s of pipelineSteps) {
    if (!anchor) states.set(s.code, 'upcoming')
    else if (onPipeline && s.code === anchor.code) states.set(s.code, 'current')
    else states.set(s.code, s.sortOrder <= anchor.sortOrder ? 'done' : 'upcoming')
  }
  return states
}
