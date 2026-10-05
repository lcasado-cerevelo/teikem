import { useCallback, useState } from 'react'
import { Keyboard } from 'react-native'

/** Estado del teclado en pantalla para un grupo de campos que comparten un solo botón. */
export function useSoftKeyboard() {
  const [show, setShow] = useState(false)
  const toggle = useCallback(() => {
    setShow((v) => {
      if (v) Keyboard.dismiss()
      return !v
    })
  }, [])
  return { show, toggle }
}
