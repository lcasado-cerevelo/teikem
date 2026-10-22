// Barra superior de la web de ESCRITORIO (2026-10-22): el logo (o el nombre) de la compañía a la izquierda y, a su lado, el menú del portal con la sección activa subrayada; sustituye al menú
// lateral y a los encabezados duplicados. Es lo mismo que las pestañas de abajo del celular (Inicio, Envíos, Almacén, Informes, Avisos, Mensajes, Ajustes), con los mismos permisos.
import { usePathname, useRouter } from 'expo-router'
import { Image, Pressable, Text, View } from 'react-native'

import { usePortalBrandInfo } from '../../kernel/brand/portalBrand'
import { useIsConsignee } from '../../kernel/auth/role'
import { useT } from '../../kernel/i18n/useT'
import { useColors } from '../../kernel/ui/theme'
import { useOffer, visibleTabs } from '../capabilities'
import { useUnreadMessages } from '../messages/unread'

interface Item {
  key: string
  route: '/home' | '/orders' | '/warehouse' | '/reports' | '/notices' | '/messages' | '/settings'
  label: string
  /** Rutas que cuentan como esta sección (detalles de envío, facturas…). */
  also?: string[]
  badge?: number
}

/** ¿La ruta actual pertenece a esta sección? */
export function isActive(item: Pick<Item, 'route' | 'also'>, pathname: string): boolean {
  return pathname === item.route || pathname.startsWith(`${item.route}/`) || (item.also ?? []).some((p) => pathname === p || pathname.startsWith(`${p}/`))
}

export function TopBar() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const pathname = usePathname()
  const offer = useOffer()
  const consignee = useIsConsignee()
  const unread = useUnreadMessages()
  const { logoUri, companyName } = usePortalBrandInfo()
  const show = visibleTabs(offer, consignee)

  const items: Item[] = [
    ...(show.home ? [{ key: 'home', route: '/home' as const, label: t('tabs.home'), also: ['/invoices', '/invoice'] }] : []),
    ...(show.orders ? [{ key: 'orders', route: '/orders' as const, label: consignee ? t('tabs.shipments') : t('tabs.orders'), also: ['/order', '/consignee-orders'] }] : []),
    ...(show.warehouse ? [{ key: 'warehouse', route: '/warehouse' as const, label: t('tabs.warehouse') }] : []),
    ...(!consignee && (show.orders || show.warehouse) ? [{ key: 'reports', route: '/reports' as const, label: t('tabs.reports') }] : []),
    { key: 'notices', route: '/notices' as const, label: t('tabs.notices'), also: ['/notice-prefs', '/consignee-notices'] },
    { key: 'messages', route: '/messages' as const, label: t('tabs.messages'), also: ['/consignee-messages'], badge: unread },
    { key: 'settings', route: '/settings' as const, label: t('tabs.settings'), also: ['/profile'] },
  ]

  return (
    <View testID="top-bar" style={{ minHeight: 60, backgroundColor: colors.brand, flexDirection: 'row', alignItems: 'center', paddingHorizontal: 24, gap: 28 }}>
      {logoUri ? (
        <Image source={{ uri: logoUri }} accessibilityLabel={companyName ?? 'Logo'} resizeMode="contain" style={{ height: 36, width: 160 }} testID="brand-logo" />
      ) : (
        !!companyName && <Text style={{ color: colors.onBrand, fontSize: 18, fontWeight: '800' }}>{companyName}</Text>
      )}
      <View style={{ flexDirection: 'row', alignItems: 'stretch', gap: 4, flexWrap: 'wrap' }}>
        {items.map((it) => {
          const active = isActive(it, pathname)
          return (
            <Pressable
              key={it.key}
              accessibilityRole="link"
              accessibilityState={{ selected: active }}
              testID={`nav-${it.key}`}
              onPress={() => router.push(it.route)}
              style={({ hovered }: { pressed: boolean; hovered?: boolean }) => ({ paddingHorizontal: 14, paddingVertical: 18, borderBottomWidth: 3, borderBottomColor: active ? colors.flow : 'transparent', backgroundColor: hovered && !active ? 'rgba(255,255,255,0.08)' : 'transparent', flexDirection: 'row', gap: 6, alignItems: 'center' })}
            >
              <Text style={{ color: colors.onBrand, fontSize: 15, fontWeight: active ? '800' : '500', opacity: active ? 1 : 0.85 }}>{it.label}</Text>
              {!!it.badge && it.badge > 0 && (
                <View style={{ backgroundColor: colors.flow, borderRadius: 10, minWidth: 20, paddingHorizontal: 6, alignItems: 'center' }}>
                  <Text style={{ color: colors.onFlow, fontSize: 12, fontWeight: '800' }}>{it.badge > 99 ? '99+' : it.badge}</Text>
                </View>
              )}
            </Pressable>
          )
        })}
      </View>
    </View>
  )
}
