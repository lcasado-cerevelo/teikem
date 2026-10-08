import { useCallback, useEffect, useRef, useState } from 'react'
import { Keyboard, Pressable, StyleSheet, Text, TextInput, useWindowDimensions, View } from 'react-native'

import { useActiveWarehouse } from '../warehouse/activeWarehouse'
import { useT } from '../i18n/useT'
import type { PickKind } from '../warehouse/pickerSearch'
import { PickerModal } from './PickerModal'
import { useScanner } from '../scanner/useScanner'
import { useFieldFocus, type MeasurableField } from './keyboardScroll'
import { placeholderFontSize } from './placeholderFont'
import { ScanMessage } from './ScanMessage'
import { BURST_QUIET_MS, NO_BURST, stepBurst, type BurstState } from './scanBurst'
import { colors, fontSize, radius, spacing, touchTarget } from './theme'

/** Valor puesto desde afuera (p. ej. tocar un producto de la lista del conteo). `seq` distinto = volver a ponerlo, aunque el
 *  valor sea el mismo de la vez anterior. */
export interface ScanPrefill {
  value: string
  seq: number
}

export interface ScanFieldProps {
  label: string
  help?: string
  /** Aviso de error de la última lectura (no encontrado, etc.): bloque grande rojo (ScanMessage). */
  error?: string | null
  /** Aviso de éxito de la última lectura (línea agregada, acomodo hecho): bloque grande verde. */
  notice?: string | null
  onSubmit: (code: string) => void
  autoFocus?: boolean
  keyboardType?: 'default' | 'numeric'
  /** Valor sugerido (p. ej. la posición destino): muestra el botón "Usar {valor}" que lo toma sin escribirlo. */
  suggestedValue?: string | null
  /** Llena el campo con un valor (sin enviarlo) y deja el cursor listo al final; se confirma con Aceptar o Enter. */
  prefill?: ScanPrefill | null
  /** Tarea 26: además de escanear, "Buscar en la lista" abre un buscador de productos, posiciones o ambos; elegir una fila equivale a escanear su código. */
  pick?: PickKind
  /** Identificador del campo para los recorridos Maestro (`tapOn: id:`). */
  testID?: string
}

/** Una misma lectura que llega dos veces seguidas (por teclas y por intent, si el perfil del lector quedara mal) se toma
 *  una sola vez. Nadie escanea el mismo código dos veces en menos de esto. */
const DUPLICATE_WINDOW_MS = 400

/**
 * Campo único de captura (docs/mobile/app-almacen-plan.md §2, "un solo campo enfocado; el escaneo escribe y avanza").
 * Recibe una lectura de DataWedge en modo intent (useScanner), el teclado físico del Zebra o el teclado en pantalla
 * (Enter = escanear), y el botón Aceptar. Escanear equivale a escribir el código y tocar Aceptar.
 * docs/mobile/mejoras-ux-zebra.md §2: el teclado en pantalla NO aparece al enfocar (`showSoftInputOnFocus={false}`); el
 * botón "⌨" lo muestra para escribir a mano y lo vuelve a esconder.
 */
export function ScanField({
  label,
  help,
  error,
  notice,
  onSubmit,
  autoFocus = true,
  keyboardType = 'default',
  suggestedValue,
  prefill,
  pick,
  testID,
}: ScanFieldProps) {
  const { t } = useT()
  const activeWarehouse = useActiveWarehouse()
  const [picking, setPicking] = useState(false)
  const { width } = useWindowDimensions()
  const [value, setValue] = useState('')
  const [keyboard, setKeyboard] = useState(false)
  const inputRef = useRef<TextInput>(null)
  const last = useRef<{ code: string; at: number } | null>(null)
  const valueRef = useRef('')
  const burst = useRef<BurstState>(NO_BURST)
  const burstTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const clearBurstTimer = useCallback(() => {
    if (burstTimer.current) clearTimeout(burstTimer.current)
    burstTimer.current = null
  }, [])

  const submit = useCallback(
    (code: string) => {
      clearBurstTimer()
      burst.current = NO_BURST
      const trimmed = code.trim()
      if (!trimmed) return
      const now = Date.now()
      if (last.current && last.current.code === trimmed && now - last.current.at < DUPLICATE_WINDOW_MS) return
      last.current = { code: trimmed, at: now }
      onSubmit(trimmed)
      valueRef.current = ''
      setValue('')
    },
    [onSubmit, clearBurstTimer],
  )

  useScanner(submit)

  // Lote A9: con el teclado en pantalla pedido (⌨), dentro de un KeyboardScreen la pantalla se desplaza hasta este campo
  const getField = useCallback(() => inputRef.current as unknown as MeasurableField | null, [])
  const handleFocus = useFieldFocus(getField)

  // El lector que escribe como TECLAS (sin intent): el código llega de golpe y se acepta solo al terminar la ráfaga (scanBurst.ts). Con el
  // teclado en pantalla visible no se hace: ahí escribe una persona.
  function onChangeText(text: string) {
    const step = stepBurst(burst.current, valueRef.current, text, Date.now())
    burst.current = step.state
    valueRef.current = text
    setValue(text)
    clearBurstTimer()
    if (keyboard || !step.scanning) return
    burstTimer.current = setTimeout(() => submit(valueRef.current), BURST_QUIET_MS)
  }
  useEffect(() => clearBurstTimer, [clearBurstTimer])

  // Valor puesto desde afuera: llena el campo, lo enfoca y deja el cursor al final (sin mostrar el teclado).
  const prefillSeq = prefill?.seq
  useEffect(() => {
    if (!prefill) return
    valueRef.current = prefill.value
    burst.current = NO_BURST
    setValue(prefill.value)
    const input = inputRef.current
    input?.focus()
    input?.setSelection?.(prefill.value.length, prefill.value.length)
    // solo cuando cambia el contador (el mismo valor dos veces seguidas también se vuelve a poner)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefillSeq])

  // Al pedir el teclado: con el campo ya enfocado no basta cambiar la propiedad, hay que volver a enfocarlo.
  function toggleKeyboard() {
    const input = inputRef.current
    if (keyboard) {
      setKeyboard(false)
      Keyboard.dismiss()
      // vuelve a enfocar (sin teclado) para que el teclado físico del Zebra siga escribiendo aquí
      setTimeout(() => input?.focus(), 50)
      return
    }
    setKeyboard(true)
    input?.blur()
    setTimeout(() => input?.focus(), 50)
  }

  const keyboardLabel = t(keyboard ? 'scan.hideKeyboard' : 'scan.showKeyboard')

  return (
    <View style={styles.wrap}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.inputRow}>
        <TextInput
          ref={inputRef}
          value={value}
          onChangeText={onChangeText}
          onSubmitEditing={(e) => submit(e.nativeEvent.text)}
          onFocus={handleFocus}
          autoFocus={autoFocus}
          showSoftInputOnFocus={keyboard}
          submitBehavior="submit"
          returnKeyType="done"
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType={keyboardType}
          style={[styles.input, error && styles.inputError, !value && { fontSize: placeholderFontSize(width, help) }]}
          numberOfLines={1}
          placeholder={help}
          placeholderTextColor={colors.muted}
          accessibilityLabel={label}
          testID={testID}
        />
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={keyboardLabel}
          accessibilityState={{ selected: keyboard }}
          onPress={toggleKeyboard}
          style={[styles.keyboardBtn, keyboard && styles.keyboardBtnOn]}
        >
          <Text style={styles.keyboardLabel}>⌨</Text>
        </Pressable>
        {/* en un teléfono sin lector no hay un Enter evidente: "Aceptar" toma lo escrito como si se hubiera escaneado */}
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('scan.accept')}
          disabled={!value.trim()}
          onPress={() => submit(value)}
          style={[styles.acceptBtn, !value.trim() && styles.btnDisabled]}
        >
          <Text style={styles.acceptLabel}>{t('scan.accept')}</Text>
        </Pressable>
      </View>
      {suggestedValue ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('scan.useSuggested', { value: suggestedValue })}
          onPress={() => submit(suggestedValue)}
          style={styles.suggestBtn}
        >
          <Text style={styles.suggestLabel}>{t('scan.useSuggested', { value: suggestedValue })}</Text>
        </Pressable>
      ) : null}
      {pick ? (
        <Pressable accessibilityRole="button" accessibilityLabel={t('picker.open')} onPress={() => setPicking(true)} style={styles.suggestBtn}>
          <Text style={styles.suggestLabel}>☰ {t('picker.open')}</Text>
        </Pressable>
      ) : null}
      {pick ? (
        <PickerModal
          visible={picking}
          kind={pick}
          warehousePublicId={activeWarehouse.publicId}
          onClose={() => setPicking(false)}
          onSelect={(code) => {
            setPicking(false)
            submit(code)
          }}
        />
      ) : null}
      {error ? <ScanMessage tone="error" message={error} /> : notice ? <ScanMessage tone="ok" message={notice} /> : null}
      {help && !error ? <Text style={styles.help}>{help}</Text> : null}
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs },
  inputRow: { flexDirection: 'row', alignItems: 'stretch', gap: spacing.sm },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  input: {
    flex: 1,
    minWidth: 0,
    minHeight: touchTarget,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  inputError: { borderColor: colors.error },
  // botón pequeño (el campo es lo importante), pero con el mínimo de toque de 44 dp de ancho y el alto del campo
  keyboardBtn: {
    minHeight: touchTarget,
    width: 44,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.line,
    alignItems: 'center',
    justifyContent: 'center',
  },
  keyboardBtnOn: { borderColor: colors.brand, backgroundColor: colors.panelAlt },
  keyboardLabel: { color: colors.text, fontSize: 22 },
  acceptBtn: {
    minHeight: touchTarget,
    minWidth: 96,
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.brand,
    alignItems: 'center',
    justifyContent: 'center',
  },
  btnDisabled: { opacity: 0.45 },
  acceptLabel: { color: colors.text, fontSize: fontSize.button, fontWeight: '700' },
  suggestBtn: {
    minHeight: 48,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.brand,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
  },
  suggestLabel: { color: colors.text, fontSize: fontSize.message, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
})
