// La raíz no tiene pantalla propia: la compuerta de _layout.tsx manda al login o al inicio según haya sesión.
import { Spinner } from '../kernel/ui/ui'

export default function Index() {
  return <Spinner />
}
