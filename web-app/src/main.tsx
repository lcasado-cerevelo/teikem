import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './app/App'
import { getLang } from './kernel/i18n/i18n'
import './styles/tokens.css'
import './styles/base.css'

document.documentElement.lang = getLang()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
