import { useState } from 'react';
import { Platform, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { MaterialIcons } from '@expo/vector-icons';
import BarcodeScannerModal from './BarcodeScannerModal';
import { colors } from '@constants/theme';

// Barra de búsqueda del listado de órdenes. El valor vive en el store
// (useAppStore.orderFilters) para que sobreviva a entrar al detalle y volver.
// Incluye escáner: leer la etiqueta es más rápido y seguro que teclear un
// código de 20 dígitos con el celular en una mano.
export default function OrderSearchBar({
  value,
  onChange,
}: {
  value: string;
  onChange: (text: string) => void;
}) {
  const { t } = useTranslation();
  const [scanning, setScanning] = useState(false);

  return (
    <>
      <View style={styles.wrap}>
        <MaterialIcons name="search" size={20} color={colors.muted} />
        <TextInput
          style={styles.input}
          value={value}
          onChangeText={onChange}
          placeholder={t('ORDERS.SEARCH_PLACEHOLDER')}
          placeholderTextColor={colors.muted}
          autoCapitalize="none"
          autoCorrect={false}
          returnKeyType="search"
          clearButtonMode="never"
        />
        {value.length > 0 && (
          <Pressable onPress={() => onChange('')} hitSlop={8} accessibilityLabel={t('ORDERS.SEARCH_CLEAR')}>
            <MaterialIcons name="close" size={20} color={colors.muted} />
          </Pressable>
        )}
        {/* El escáner no existe en web: allí expo-camera no da barcodeTypes. */}
        {Platform.OS !== 'web' && (
          <Pressable
            onPress={() => setScanning(true)}
            hitSlop={8}
            accessibilityLabel={t('ORDERS.SCAN_OPEN')}
            style={styles.scanBtn}
          >
            <MaterialIcons name="qr-code-scanner" size={22} color={colors.primary} />
          </Pressable>
        )}
      </View>

      <BarcodeScannerModal
        visible={scanning}
        onClose={() => setScanning(false)}
        onScanned={(code) => onChange(code)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginHorizontal: 20,
    marginTop: 4,
    marginBottom: 6,
    paddingHorizontal: 12,
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
  },
  input: { flex: 1, paddingVertical: 10, color: colors.text },
  scanBtn: { paddingLeft: 2, borderLeftWidth: 1, borderLeftColor: colors.gray, paddingVertical: 8, paddingStart: 8 },
});
