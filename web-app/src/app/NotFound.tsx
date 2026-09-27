import { Link } from 'react-router-dom'
import { useT } from '../kernel/i18n/useT'

export default function NotFound() {
  const t = useT()
  return (
    <div className="empty">
      <div>
        <h2>{t('shell.notFound.title')}</h2>
        <p>{t('shell.notFound.body')}</p>
        <Link className="btn" to="/">
          {t('access.backHome')}
        </Link>
      </div>
    </div>
  )
}
