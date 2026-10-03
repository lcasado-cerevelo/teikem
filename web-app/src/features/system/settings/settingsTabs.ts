// Pestañas de Ajustes de la compañía y su parámetro `?tab=` (lógica pura).
export const SETTINGS_TABS = ['general', 'region', 'calendar', 'modules', 'ops', 'brand'] as const
export type SettingsTab = (typeof SETTINGS_TABS)[number]

/** `?tab=` → pestaña (desconocida o sin parámetro = General). */
export function settingsTabFromParam(v: string | null): SettingsTab {
  return (SETTINGS_TABS as readonly string[]).includes(v ?? '') ? (v as SettingsTab) : 'general'
}
