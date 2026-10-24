import { useState, type ReactNode } from 'react';
import { Linking, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { MaterialIcons } from '@expo/vector-icons';
import { Text, TextInput } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import Chip from '@components/Chip';
import BarcodeScannerModal from '@components/BarcodeScannerModal';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Armazón de las consultas del personal (Lote 217): título, compañías (si son varias), buscador libre (con lector de código si se pide) y el contenido.
export default function LookupShell({ title, companies, tenantId, onTenant, text, onText, placeholder, scan, children }: {
  title: string;
  companies: { tenantId: number; name: string }[];
  tenantId: number | null;
  onTenant: (id: number) => void;
  text: string;
  onText: (v: string) => void;
  placeholder: string;
  scan?: boolean;
  children: ReactNode;
}) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const [scanning, setScanning] = useState(false);
  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={title} />
      {companies.length > 1 ? (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
          {companies.map((c) => <Chip key={c.tenantId} label={c.name} active={c.tenantId === tenantId} onPress={() => onTenant(c.tenantId)} />)}
        </ScrollView>
      ) : null}
      <View style={styles.wrap}>
        <MaterialIcons name="search" size={20} color={colors.muted} />
        <TextInput style={styles.input} value={text} onChangeText={onText} placeholder={placeholder} placeholderTextColor={colors.muted} autoCapitalize="none" autoCorrect={false} returnKeyType="search" accessibilityLabel={placeholder} />
        {scan ? (
          <Pressable onPress={() => setScanning(true)} hitSlop={8} accessibilityRole="button" accessibilityLabel="scan"><MaterialIcons name="qr-code-scanner" size={24} color={colors.primary} /></Pressable>
        ) : null}
      </View>
      <View style={styles.content}>{children}</View>
      {scan ? <BarcodeScannerModal visible={scanning} onClose={() => setScanning(false)} onScanned={(v) => { setScanning(false); onText(v); }} /> : null}
    </SafeAreaView>
  );
}

/** Una línea «Etiqueta: valor». Si trae enlace (teléfono, correo, mapa) el valor se toca. */
export function InfoLine({ label, value, href }: { label?: string; value: string; href?: string | null }) {
  const styles = useStyles(createStyles);
  return (
    <Text style={styles.line}>
      {label ? <Text style={styles.key}>{label}: </Text> : null}
      {href ? <Text style={styles.link} accessibilityRole="link" onPress={() => void Linking.openURL(href).catch(() => undefined)}>{value}</Text> : value}
    </Text>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  chips: { paddingHorizontal: 20, gap: 8, paddingBottom: 6 },
  wrap: { flexDirection: 'row', alignItems: 'center', gap: 8, borderWidth: 1, borderColor: colors.gray, borderRadius: 12, marginHorizontal: 20, marginBottom: 8, paddingHorizontal: 12, minHeight: 46 },
  input: { flex: 1, color: colors.text, fontSize: 16, paddingVertical: 8 },
  content: { flex: 1 },
  line: { fontSize: 14, color: colors.text, marginTop: 4 },
  key: { fontWeight: '700', color: colors.muted },
  link: { color: colors.primary, textDecorationLine: 'underline' },
});
