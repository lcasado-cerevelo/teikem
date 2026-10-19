// Roles y usuarios (/system/users, `admin.users|admin.roles`, módulo SYSTEM): una sola cabecera como
// `usuariosScreen` de la maqueta — título y subtítulo a la izquierda; a la derecha el segmento Roles | Usuarios
// (Roles primero y por defecto: "lo primero que debe permitir trabajar son los roles") y el botón de alta de la
// pestaña activa ("Nuevo rol" / "Nuevo usuario"). El alta la abre la página y el modal vive en cada pestaña.
// Cada pestaña además se filtra por su propio permiso (GET /roles no exige ninguno; GET /users exige admin.users):
// sin `admin.roles` la pestaña Roles ni se muestra; sin `admin.users` tampoco Usuarios. Con solo una pestaña
// visible, el segmento no se pinta.
import { useState } from 'react'
import { Can, ModuleKeys, useCan, useModule } from '../../kernel/access'
import { useT } from '../../kernel/i18n/useT'
import { IconShield, IconUsers, Tabs } from '../../kernel/ui'
import { PortalUsersTab } from './PortalUsersTab'
import { RolesTab } from './RolesTab'
import { UsersTab } from './UsersTab'

type Segment = 'roles' | 'users' | 'clients' | 'consignees'

export default function UsersPage() {
  const t = useT()
  const canRoles = useCan('admin.roles')
  const canUsers = useCan('admin.users')
  // Lote 121: las personas de portal (de los clientes) y los accesos de consignatarios, en sus propias pestañas con su tipo
  const portalOn = useModule(ModuleKeys.ClientPortal)
  const canPortal = useCan('portalusers.manage') && portalOn
  const available = ([canRoles && 'roles', canUsers && 'users', canPortal && 'clients', canPortal && 'consignees'].filter(Boolean) as Segment[])
  const [segment, setSegment] = useState<Segment>(canRoles ? 'roles' : 'users')
  const active: Segment = available.includes(segment) ? segment : (available[0] ?? 'users')
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
          {available.length > 1 && (
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
                {
                  key: 'clients',
                  label: (
                    <>
                      <IconUsers /> {t('system.users.tabClients')}
                    </>
                  ),
                },
                {
                  key: 'consignees',
                  label: (
                    <>
                      <IconUsers /> {t('system.users.tabConsignees')}
                    </>
                  ),
                },
              ].filter((x) => available.includes(x.key as Segment)) as { key: Segment; label: React.ReactNode }[]}
            />
          )}
          {showRoles ? (
            <Can perm="admin.roles">
              <button type="button" className="btn flow" onClick={() => setCreating('roles')}>
                {t('system.users.roles.new')}
              </button>
            </Can>
          ) : active === 'users' ? (
            <Can perm="admin.users">
              <button type="button" className="btn flow" onClick={() => setCreating('users')}>
                {t('system.users.users.new')}
              </button>
            </Can>
          ) : null}
        </div>
      </div>

      {showRoles ? (
        <RolesTab creating={creating === 'roles'} onCreateClose={() => setCreating(null)} />
      ) : active === 'clients' ? (
        <PortalUsersTab kind="CLIENT" />
      ) : active === 'consignees' ? (
        <PortalUsersTab kind="CONSIGNEE" />
      ) : (
        <UsersTab creating={creating === 'users'} onCreateClose={() => setCreating(null)} />
      )}
    </div>
  )
}
