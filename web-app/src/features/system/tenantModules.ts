// Módulos de la compañía (Ajustes → Módulos): dependencias y cascada, réplica de `ModuleService.SetEnabledAsync` (encender
// exige la dependencia encendida; apagar apaga en cascada los dependientes; un núcleo no se apaga). Lógica pura.

export interface ModuleLike {
  key?: string | null
  name?: string | null
  dependsOn?: string | null
  isCore?: boolean
  isEnabled?: boolean
}

const same = (a: string | null | undefined, b: string | null | undefined) => (a ?? '').toUpperCase() === (b ?? '').toUpperCase()

/** Nombre del módulo por su clave (la clave si no está). */
export function moduleName(key: string | null | undefined, modules: readonly ModuleLike[]): string {
  return modules.find((m) => same(m.key, key))?.name || key || ''
}

/** La dependencia del módulo está apagada: no se puede encender todavía. */
export function dependencyOff(m: ModuleLike, modules: readonly ModuleLike[]): boolean {
  if (!m.dependsOn) return false
  return !(modules.find((x) => same(x.key, m.dependsOn))?.isEnabled ?? false)
}

/** Módulos ENCENDIDOS que se apagarían en cascada al apagar `key` (dependientes directos e indirectos). */
export function enabledDependents(key: string, modules: readonly ModuleLike[]): ModuleLike[] {
  const out: ModuleLike[] = []
  const stack = [key]
  const seen = new Set<string>([key.toUpperCase()])
  while (stack.length > 0) {
    const k = stack.pop() as string
    for (const m of modules) {
      if (!same(m.dependsOn, k) || !m.key || seen.has(m.key.toUpperCase())) continue
      seen.add(m.key.toUpperCase())
      if (m.isEnabled) out.push(m)
      stack.push(m.key)
    }
  }
  return out
}
