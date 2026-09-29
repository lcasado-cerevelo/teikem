// Roles y usuarios (/system/users, `admin.users|admin.roles`, módulo SYSTEM): una sola cabecera como
// `usuariosScreen` de la maqueta — título y subtítulo a la izquierda; a la derecha el segmento Roles | Usuarios
// (Roles primero y por defecto: "lo primero que debe permitir trabajar son los roles") y el botón de alta de la
// pestaña activa ("Nuevo rol" / "Nuevo usuario"). El alta la abre la página y el modal vive en cada pestaña.
// Cada pestaña además se filtra por su propio permiso (GET /roles no exige ninguno; GET /users exige admin.users):
// sin `admin.roles` la pestaña Roles ni se muestra; sin `admin.users` tampoco Usuarios. Con solo una pestaña
// visible, el segmento no se pinta.
import { useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n/useT'
import { IconShield, IconUsers, Tabs } from '../../kernel/ui'
import { RolesTab } from './RolesTab'
import { UsersTab } from './UsersTab'

type Segment = 'roles' | 'users'

export default function UsersPage() {
  const t = useT()
  const canRoles = useCan('admin.roles')
  const canUsers = useCan('admin.users')
  const [segment, setSegment] = useState<Segment>(canRoles ? 'roles' : 'users')
  const active: Segment = segment === 'roles' && !canRoles ? 'users' : segment === 'users' && !canUsers ? 'roles' : segment
  // Alta pedida desde la cabecera: qué pestaña debe abrir su modal de alta (null = ninguno).
  const [creating, setCreating] = useState<Segment | null>(null)
  const showRoles = active === 'roles' && canRoles

  const changeSegment = (next: Segment) => {
    setCreating(null)
    setSegment(next)
  }

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('system.users.title')}</h1>
          <p>{t('system.users.subtitle')}</p>
        </div>
        <div className="act">
          {canRoles && canUsers && (
            <Tabs<Segment>
              label={t('system.users.title')}
              value={active}
              onChange={changeSegment}
              tabs={[
                {
                  key: 'roles',
                  label: (
                    <>
                      <IconShield /> {t('system.users.tabRoles')}
                    </>
                  ),
                },
                {
                  key: 'users',
                  label: (
                    <>
                      <IconUsers /> {t('system.users.tabUsers')}
                    </>
                  ),
                },
              ]}
            />
          )}
          {showRoles ? (
            <Can perm="admin.roles">
              <button type="button" className="btn flow" onClick={() => setCreating('roles')}>
                {t('system.users.roles.new')}
              </button>
            </Can>
          ) : (
            <Can perm="admin.users">
              <button type="button" className="btn flow" onClick={() => setCreating('users')}>
                {t('system.users.users.new')}
              </button>
            </Can>
          )}
        </div>
      </div>

      {showRoles ? (
        <RolesTab creating={creating === 'roles'} onCreateClose={() => setCreating(null)} />
      ) : (
        <UsersTab creating={creating === 'users'} onCreateClose={() => setCreating(null)} />
      )}
    </div>
  )
}
