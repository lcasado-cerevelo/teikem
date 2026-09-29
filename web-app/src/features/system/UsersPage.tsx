// Roles y usuarios (/system/users, `admin.users|admin.roles`, módulo SYSTEM): segmento Roles | Usuarios. Cada
// pestaña además se filtra por su propio permiso (GET /roles no exige ninguno; GET /users exige admin.users):
// sin `admin.roles` la pestaña Roles ni se muestra; sin `admin.users` tampoco Usuarios. Con solo una pestaña
// visible, el segmento no se pinta.
import { useState } from 'react'
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n/useT'
import { Tabs } from '../../kernel/ui'
import { RolesTab } from './RolesTab'
import { UsersTab } from './UsersTab'

type Segment = 'roles' | 'users'

export default function UsersPage() {
  const t = useT()
  const canRoles = useCan('admin.roles')
  const canUsers = useCan('admin.users')
  const [segment, setSegment] = useState<Segment>(canUsers ? 'users' : 'roles')
  const active: Segment = segment === 'roles' && !canRoles ? 'users' : segment === 'users' && !canUsers ? 'roles' : segment

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('system.users.title')}</h1>
          <p>{t('system.users.subtitle')}</p>
        </div>
      </div>

      {canRoles && canUsers && (
        <div style={{ marginBottom: 14 }}>
          <Tabs<Segment>
            label={t('system.users.title')}
            value={active}
            onChange={setSegment}
            tabs={[
              { key: 'roles', label: t('system.users.tabRoles') },
              { key: 'users', label: t('system.users.tabUsers') },
            ]}
          />
        </div>
      )}

      {active === 'roles' && canRoles ? <RolesTab /> : <UsersTab />}
    </div>
  )
}
