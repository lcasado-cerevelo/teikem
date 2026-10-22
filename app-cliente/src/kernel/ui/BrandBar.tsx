// Barra superior de la web con la marca de la compañía (2026-10-22): su logo (o su nombre si no subió uno) sobre el color de la barra de su marca. En el celular no se pinta (cada pantalla ya trae su encabezado).
import { Image, Platform, Text, View } from 'react-native'

import { usePortalBrandInfo } from '../brand/portalBrand'
import { useColors } from './theme'

export function BrandBar() {
  const colors = useColors()
  const { logoUri, companyName } = usePortalBrandInfo()
  if (Platform.OS !== 'web') return null
  return (
    <View testID="brand-bar" style={{ height: 56, backgroundColor: colors.brand, flexDirection: 'row', alignItems: 'center', paddingHorizontal: 20, gap: 12 }}>
      {logoUri ? (
        <Image source={{ uri: logoUri }} accessibilityLabel={companyName ?? 'Logo'} resizeMode="contain" style={{ height: 36, width: 180 }} testID="brand-logo" />
      ) : (
        !!companyName && <Text style={{ color: colors.onBrand, fontSize: 18, fontWeight: '700' }}>{companyName}</Text>
      )}
    </View>
  )
}
