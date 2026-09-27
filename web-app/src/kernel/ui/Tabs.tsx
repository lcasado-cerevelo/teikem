import type { ReactNode } from 'react'

export interface TabItem<K extends string> {
  key: K
  label: ReactNode
}

export interface TabsProps<K extends string> {
  tabs: readonly TabItem<K>[]
  value: K
  onChange: (key: K) => void
  label?: string
}

/** Pestañas de una ficha (`.seg`). Si no caben, se desplazan dentro de su propia franja (sin scroll de página). */
export function Tabs<K extends string>({ tabs, value, onChange, label }: TabsProps<K>) {
  return (
    <div className="seg" role="tablist" aria-label={label}>
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type="button"
          role="tab"
          aria-selected={tab.key === value}
          className={tab.key === value ? 'on' : undefined}
          onClick={() => onChange(tab.key)}
        >
          {tab.label}
        </button>
      ))}
    </div>
  )
}
