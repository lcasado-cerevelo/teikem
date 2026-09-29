import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './app/App'
import { getLang } from './kernel/i18n/i18n'
import { initTheme } from './kernel/ui/theme'
import './styles/tokens.css'
import './styles/base.css'

document.documentElement.lang = getLang()
// tema guardado (o el del sistema) antes de pintar: sin parpadeo de la paleta oscura
initTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
