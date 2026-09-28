import { Vibration } from 'react-native'

// docs/mobile/app-almacen-plan.md §2: "sonido y vibración distintos para ok y error". El MC3300 vibra bien; el sonido
// (expo-av) queda para cuando se confirme que el modelo del Zebra lo necesita además de la vibración (decisión abierta).
export function vibrateOk(): void {
  Vibration.vibrate(40)
}

export function vibrateError(): void {
  Vibration.vibrate([0, 60, 60, 60])
}
