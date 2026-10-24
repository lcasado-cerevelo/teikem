// Consignatario NUEVO en la misma pantalla de captura (2026-10-23, pedido de Luis: «si no lo encuentra, que lo entre ahí mismo, sin mouse, y ahí mismo lo grabe; no tiene que ir a ninguna parte»).
// Al escoger «+ Nuevo consignatario» el combo abre este bloque: el foco cae solo en el primer campo obligatorio que falta (el nombre ya viene de lo escrito), Tab recorre nombre → dirección → pueblo → estado →
// código postal → teléfono, y ENTER en cualquier campo graba el consignatario (si falta algo obligatorio, lleva el foco ahí); Tab desde el último campo también graba; Escape cancela. Quien lo usa
// (la rejilla rápida y la detallada) guarda el consignatario en el servidor, lo deja escogido y devuelve el foco al siguiente campo del envío.
import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { Text, TextInput, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { Button, Notice, useColors } from '../../kernel/ui/ui'
import type { NewConsigneeDraft } from './newOrderRules'

export type NewConsigneeField = 'name' | 'line1' | 'city' | 'state' | 'postalCode' | 'phone'
/** Orden en que se recorren los campos. */
export const NEW_CONSIGNEE_FIELDS: readonly NewConsigneeField[] = ['name', 'line1', 'city', 'state', 'postalCode', 'phone']
const REQUIRED: readonly NewConsigneeField[] = ['name', 'line1', 'city']
const WIDTH: Record<NewConsigneeField, number> = { name: 220, line1: 280, city: 160, state: 90, postalCode: 110, phone: 160 }

/** Los obligatorios que todavía no se han escrito, en el orden de los campos. */
export const missingConsigneeFields = (n: Pick<NewConsigneeDraft, 'name' | 'line1' | 'city'>): NewConsigneeField[] => REQUIRED.filter((f) => !(n as Record<string, string>)[f].trim())

export interface NewConsigneeHandle {
  /** Lleva el foco al primer obligatorio que falta; false si no falta ninguno. */
  focusFirstMissing: () => boolean
}

export interface NewConsigneeInlineProps {
  value: NewConsigneeDraft
  onChange: (v: NewConsigneeDraft) => void
  /** Enter / Tab desde el último campo / el botón: el padre lo guarda en el servidor (solo se llama con los obligatorios completos). */
  onConfirm: () => void
  /** Escape o «Cancelar». */
  onCancel: () => void
  busy?: boolean
  error?: string | null
  /** Prefijo de los testID de los campos: `${prefix}-name`, `${prefix}-line1`, … */
  testPrefix?: string
  testID?: string
}

export const NewConsigneeInline = forwardRef<NewConsigneeHandle, NewConsigneeInlineProps>(function NewConsigneeInline({ value, onChange, onConfirm, onCancel, busy, error, testPrefix = 'new', testID }, ref) {
  const colors = useColors()
  const { t } = useT()
  const refs = useRef<Partial<Record<NewConsigneeField, TextInput | null>>>({})
  const [warn, setWarn] = useState(false)
  const focus = (f: NewConsigneeField) => setTimeout(() => refs.current[f]?.focus(), 0)

  const focusFirstMissing = () => {
    const m = missingConsigneeFields(value)[0]
    if (!m) return false
    setWarn(true)
    focus(m)
    return true
  }
  useImperativeHandle(ref, () => ({ focusFirstMissing }))

  // al abrirse: el foco al primer campo obligatorio vacío (el nombre ya viene de lo escrito, así que normalmente la dirección)
  useEffect(() => {
    focus(missingConsigneeFields(value)[0] ?? 'name')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const confirm = () => {
    if (busy) return
    if (focusFirstMissing()) return
    setWarn(false)
    onConfirm()
  }

  const set = (f: NewConsigneeField, v: string) => onChange({ ...value, [f]: v })
  return (
    <View style={{ gap: 8, backgroundColor: colors.neutralBg, borderRadius: 10, padding: 10 }} testID={testID}>
      <Text style={{ fontWeight: '700', color: colors.text }}>{t('newOrder.quick.newBlock')}</Text>
      <View style={{ flexDirection: 'row', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        {NEW_CONSIGNEE_FIELDS.map((f, i) => {
          const last = i === NEW_CONSIGNEE_FIELDS.length - 1
          const required = REQUIRED.includes(f)
          const missing = warn && required && !value[f].trim()
          return (
            <View key={f} style={{ width: WIDTH[f], flexGrow: f === 'line1' ? 1 : 0, maxWidth: '100%', gap: 2 }}>
              <Text style={{ fontSize: 12, color: colors.muted }}>{f === 'phone' ? t('newOrder.phoneOptional') : t(`newOrder.${f}`)}{required ? ' *' : ''}</Text>
              <TextInput
                ref={(el) => { refs.current[f] = el }}
                accessibilityLabel={t(`newOrder.${f}`)}
                testID={`${testPrefix}-${f}`}
                value={value[f]}
                editable={!busy}
                onChangeText={(v) => set(f, v)}
                onSubmitEditing={confirm}
                blurOnSubmit={false}
                onKeyPress={(e) => {
                  const k = (e.nativeEvent as { key?: string; shiftKey?: boolean }).key
                  const shift = (e.nativeEvent as { shiftKey?: boolean }).shiftKey
                  if (k === 'Escape') onCancel()
                  else if (k === 'Tab' && !shift && last) {   // Tab desde el último campo graba y sigue con el envío
                    e.preventDefault()
                    confirm()
                  }
                }}
                keyboardType={f === 'phone' ? 'phone-pad' : f === 'postalCode' ? 'number-pad' : 'default'}
                autoCapitalize={f === 'state' ? 'characters' : 'words'}
                maxLength={f === 'state' ? 2 : f === 'postalCode' ? 10 : 200}
                placeholderTextColor={colors.muted}
                style={{ minHeight: 38, borderRadius: 8, borderWidth: 1, borderColor: missing ? colors.danger : colors.line, backgroundColor: colors.card, color: colors.text, paddingHorizontal: 8, fontSize: 14 }}
              />
            </View>
          )
        })}
      </View>
      {warn && missingConsigneeFields(value).length > 0 && <Text accessibilityRole="alert" style={{ fontSize: 12, color: colors.danger }}>{t('newOrder.consigneeBlock.missing')}</Text>}
      {!!error && <Notice>{error}</Notice>}
      <View style={{ flexDirection: 'row', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
        <View style={{ minWidth: 190 }}><Button title={t('newOrder.consigneeBlock.save')} onPress={confirm} busy={busy} testID={`${testPrefix}-save`} /></View>
        <Button title={t('common.cancel')} variant="link" onPress={onCancel} />
        <Text style={{ fontSize: 12, color: colors.muted, flexShrink: 1 }}>{t('newOrder.consigneeBlock.hint')}</Text>
      </View>
    </View>
  )
})
