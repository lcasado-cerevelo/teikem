// Buscador de consignatario con autocompletar (2026-10-23, dictado de Luis: «que luzca igual a la pantalla del personal», nada de listar todos los consignatarios hacia abajo). Se escribe
// y se escoge de la lista (flechas y Enter, o un toque); «+ Nuevo» avisa al padre para que pinte los campos del consignatario nuevo. Lo usan la entrada detallada y la rápida.
import { useState } from 'react'
import { Pressable, Text, TextInput, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { useColors } from '../../kernel/ui/ui'
import type { PortalPlaceDto } from '../portalApi'
import { emptyConsignee, type NewConsigneeDraft } from './newOrderRules'
import { matchConsignees } from './quickRules'

export interface ConsigneeValue {
  /** publicId del elegido ('' = ninguno). */
  consigneeId: string
  /** Lo escrito en el buscador (el nombre del elegido cuando hay uno). */
  query: string
  /** Consignatario nuevo en captura (null = no). */
  newConsignee: NewConsigneeDraft | null
}

export const emptyConsigneeValue = (): ConsigneeValue => ({ consigneeId: '', query: '', newConsignee: null })

export interface ConsigneeComboProps {
  consignees: readonly PortalPlaceDto[]
  value: ConsigneeValue
  onChange: (v: ConsigneeValue) => void
  /** Después de elegir uno de la lista: el padre lleva el foco al siguiente campo. */
  onPicked?: () => void
  /** Después de escoger «+ Nuevo consignatario»: el padre abre el bloque de captura (que toma el foco solo). */
  onNew?: () => void
  /** Enter sin lista abierta (la rejilla guarda la fila). */
  onEnter?: () => void
  compact?: boolean
  invalid?: boolean
  disabled?: boolean
  inputRef?: (el: { focus: () => void } | null) => void
  /** Prefijo de los testID: `${testID}` el campo, `${testID}-suggest-N` las opciones, `${testID}-new` el «+ Nuevo». */
  testID?: string
}

export const placeText = (c: PortalPlaceDto) => [c.line1, c.city].filter(Boolean).join(', ')

export function ConsigneeCombo({ consignees, value, onChange, onPicked, onNew, onEnter, compact, invalid, disabled, inputRef, testID = 'consignee' }: ConsigneeComboProps) {
  const colors = useColors()
  const { t } = useT()
  const [open, setOpen] = useState(false)
  const [hi, setHi] = useState(0)

  const matches = !value.consigneeId && !value.newConsignee ? matchConsignees(consignees, value.query) : []
  const showList = open && !disabled && !value.consigneeId && !value.newConsignee && value.query.trim().length > 0

  const choose = (c: PortalPlaceDto) => {
    onChange({ consigneeId: c.publicId as string, query: c.name ?? '', newConsignee: null })
    setOpen(false)
    onPicked?.()
  }
  const startNew = () => {
    onChange({ consigneeId: '', query: value.query, newConsignee: { ...emptyConsignee(), name: value.query.trim() } })
    setOpen(false)
    onNew?.()
  }

  return (
    <View style={{ zIndex: 20 }}>
      <TextInput
        ref={(el) => inputRef?.(el)}
        accessibilityLabel={t('newOrder.consignee')}
        testID={testID}
        value={value.query}
        editable={!disabled}
        onChangeText={(v) => { onChange({ consigneeId: '', query: v, newConsignee: null }); setOpen(true); setHi(0) }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}   // deja que el toque en una opción llegue antes de cerrar
        onSubmitEditing={() => {
          // con texto escrito y nada escogido: Enter escoge la resaltada; la última posición (o ninguna coincidencia) es «+ Nuevo consignatario» (aunque la lista se haya cerrado con Escape)
          if (!value.consigneeId && !value.newConsignee && value.query.trim().length > 0 && !disabled) {
            if (hi < matches.length) choose(matches[hi])
            else startNew()
          } else onEnter?.()
        }}
        blurOnSubmit={false}
        onKeyPress={(e) => {
          const k = (e.nativeEvent as { key?: string }).key
          if (k === 'ArrowDown') setHi((h) => Math.min(h + 1, Math.max(matches.length, 0)))   // la última posición es «+ Nuevo»
          if (k === 'ArrowUp') setHi((h) => Math.max(h - 1, 0))
          if (k === 'Escape') setOpen(false)
        }}
        autoCapitalize="none"
        autoCorrect={false}
        placeholder={t('newOrder.quick.consigneePlaceholder')}
        placeholderTextColor={colors.muted}
        style={{ minHeight: compact ? 40 : 48, borderRadius: compact ? 8 : 10, borderWidth: 1, borderColor: invalid ? colors.danger : value.consigneeId ? colors.ok : colors.line, backgroundColor: colors.card, color: colors.text, paddingHorizontal: compact ? 8 : 12, fontSize: compact ? 14 : 16 }}
      />
      {showList && (
        <View style={{ position: 'absolute', top: compact ? 42 : 50, left: 0, right: 0, backgroundColor: colors.card, borderWidth: 1, borderColor: colors.line, borderRadius: 8, zIndex: 50, overflow: 'hidden', elevation: 6 }} testID={`${testID}-list`}>
          {matches.map((c, i) => (
            <Pressable key={c.publicId} onPress={() => choose(c)} accessibilityRole="button" testID={`${testID}-suggest-${i}`} style={{ padding: 8, backgroundColor: i === hi ? colors.neutralBg : colors.card }}>
              <Text style={{ fontWeight: '700', color: colors.text }}>{c.name}{c.code ? <Text style={{ color: colors.muted, fontWeight: '400' }}> · {c.code}</Text> : null}</Text>
              <Text style={{ fontSize: 12, color: colors.muted }}>{placeText(c)}</Text>
            </Pressable>
          ))}
          <Pressable onPress={startNew} accessibilityRole="button" testID={`${testID}-new`} style={{ padding: 8, backgroundColor: hi >= matches.length ? colors.neutralBg : colors.card, borderTopWidth: matches.length ? 1 : 0, borderTopColor: colors.line }}>
            <Text style={{ fontWeight: '700', color: colors.link }}>{t('newOrder.quick.newConsignee', { name: value.query.trim() })}</Text>
          </Pressable>
        </View>
      )}
    </View>
  )
}
