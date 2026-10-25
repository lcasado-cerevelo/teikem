// Campo de contraseña de TODO el portal (Lote 219, pedido de Luis): con el ojito (icono dentro del campo) para ver lo que se escribió y, en la web, el aviso «Las mayúsculas están activadas».
// En el teléfono el sistema no deja saber si las mayúsculas están activadas (es del teclado), así que ahí solo está el ojito. El aviso sale al teclear y se quita al soltar el campo.
import { Ionicons } from '@expo/vector-icons'
import { useState } from 'react'
import { Platform, Pressable, Text, TextInput, View, type TextInputProps } from 'react-native'

import { useT } from '../i18n/useT'
import { useColors } from './theme'

/** ¿Las mayúsculas están activadas según esta tecla? (solo en la web; el evento del navegador lo dice). */
export function capsLockFrom(event: unknown): boolean {
  const e = event as { getModifierState?: (key: string) => boolean; nativeEvent?: { getModifierState?: (key: string) => boolean } } | null
  const fn = e?.getModifierState ?? e?.nativeEvent?.getModifierState
  if (typeof fn !== 'function') return false
  try {
    return !!fn.call(e?.getModifierState ? e : e?.nativeEvent, 'CapsLock')
  } catch {
    return false
  }
}

export function PasswordField({ label, error, pin = false, testID, ...input }: { label: string; error?: string | null; pin?: boolean } & Omit<TextInputProps, 'secureTextEntry'>) {
  const { t } = useT()
  const c = useColors()
  const [shown, setShown] = useState(false)
  const [caps, setCaps] = useState(false)
  // en la web, `onKeyPress` de RN-web es el keydown del <input> y trae el estado de las mayúsculas; en el teléfono el teclado del sistema no lo dice y no se ofrece
  const keyProps = Platform.OS === 'web' && !pin ? { onKeyPress: (e: unknown) => setCaps(capsLockFrom(e)) } : {}
  return (
    <View style={{ gap: 4 }}>
      <Text style={{ fontSize: 13, fontWeight: '600', color: c.text }}>{label}</Text>
      <View>
        <TextInput
          accessibilityLabel={label}
          placeholderTextColor={c.muted}
          testID={testID}
          autoCapitalize="none"
          autoCorrect={false}
          {...input}
          {...keyProps}
          secureTextEntry={!shown}
          onBlur={(e) => {
            setCaps(false)
            input.onBlur?.(e)
          }}
          style={{ minHeight: 48, borderRadius: 10, borderWidth: 1, borderColor: error ? c.danger : c.line, backgroundColor: c.card, paddingLeft: 12, paddingRight: 48, fontSize: 16, color: c.text }}
        />
        <Pressable
          testID={testID ? `${testID}-eye` : undefined}
          accessibilityRole="button"
          accessibilityLabel={shown ? t('login.hide') : t('login.show')}
          accessibilityState={{ selected: shown }}
          onPress={() => setShown((v) => !v)}
          hitSlop={8}
          style={{ position: 'absolute', right: 6, top: 0, bottom: 0, width: 40, alignItems: 'center', justifyContent: 'center' }}
        >
          <Ionicons name={shown ? 'eye-off-outline' : 'eye-outline'} size={22} color={c.muted} />
        </Pressable>
      </View>
      {caps && (
        <Text testID="caps-warning" accessibilityRole="alert" style={{ color: c.warn, backgroundColor: c.warnBg, fontSize: 12, fontWeight: '600', paddingVertical: 4, paddingHorizontal: 8, borderRadius: 6, overflow: 'hidden', alignSelf: 'flex-start' }}>
          {t('login.capsLock')}
        </Text>
      )}
      {!!error && <Text style={{ color: c.danger, fontSize: 12 }}>{error}</Text>}
    </View>
  )
}
