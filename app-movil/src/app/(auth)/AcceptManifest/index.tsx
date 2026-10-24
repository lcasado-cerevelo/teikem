import { useMemo, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, StyleSheet, Switch, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import ScreenHeader from '@components/ScreenHeader';
import OptionSelect from '@components/OptionSelect';
import { useDeclineReasons, useDriverWork } from '@hooks/queries';
import { acceptManifest } from '@services/driverApi';
import { useAppStore } from '@/stores';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

interface Decline {
  reasonIndex: number | null;
  note: string;
}

// Aceptar el manifiesto = la SALIDA: los envíos que acepta pasan a «En tránsito». Los que NO acepta llevan un motivo de la lista de la compañía y vuelven a la oficina.
// Necesita conexión (es una decisión que la oficina ve de inmediato), por eso no pasa por la cola de envío.
export default function AcceptManifest() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const qc = useQueryClient();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { data, isPending } = useDriverWork();
  const addShipmentIds = useAppStore((s) => s.addShipmentIds);

  const manifest = useMemo(() => data?.manifests.find((m) => m.id === id) ?? null, [data, id]);
  const shipments = useMemo(() => (data?.shipments ?? []).filter((s) => s.manifestId === id).sort((a, b) => a.sequence - b.sequence), [data, id]);
  const { data: reasons } = useDeclineReasons(manifest?.tenantId);

  const [declines, setDeclines] = useState<Record<string, Decline>>({});
  const [busy, setBusy] = useState(false);

  const options = useMemo(() => (reasons ?? []).map((r, i) => ({ id: i + 1, label: r.label })), [reasons]);
  const setDecline = (shipmentId: string, on: boolean) =>
    setDeclines((prev) => {
      const next = { ...prev };
      if (on) next[shipmentId] = { reasonIndex: null, note: '' };
      else delete next[shipmentId];
      return next;
    });

  const submit = async () => {
    if (!manifest || !reasons) return;
    const list = Object.entries(declines);
    if (list.some(([, d]) => d.reasonIndex == null)) {
      Alert.alert(t('ACCEPT.REASON_REQUIRED'));
      return;
    }
    const doIt = async () => {
      setBusy(true);
      try {
        const result = await acceptManifest(
          manifest,
          list.map(([shipmentPublicId, d]) => ({ shipmentPublicId, reasonCode: reasons[(d.reasonIndex as number) - 1].code, note: d.note.trim() || undefined })),
        );
        await qc.invalidateQueries({ queryKey: ['work'] });
        const kept = shipments.filter((s) => !declines[s.id]);
        addShipmentIds(kept.map((s) => s.id));
        Alert.alert(t('ACCEPT.DONE_TITLE'), result.started ? t('ACCEPT.DONE_BODY', { accepted: result.accepted, declined: result.declined }) : t('ACCEPT.DONE_NONE'), [
          { text: 'OK', onPress: () => (result.started ? router.replace('/(auth)/OrdersList') : router.back()) },
        ]);
      } catch (err) {
        const ax = err as AxiosError<{ title?: string }>;
        Alert.alert(t('ACCEPT.ERROR_TITLE'), ax.response ? (ax.response.data?.title ?? t('ACCEPT.ERROR')) : t('ACCEPT.NO_SIGNAL'));
      } finally {
        setBusy(false);
      }
    };
    Alert.alert(t('ACCEPT.CONFIRM_TITLE'), t('ACCEPT.CONFIRM_BODY', { accepted: shipments.length - list.length, declined: list.length }), [
      { text: t('PROFILE.LOGOUT_CANCEL'), style: 'cancel' },
      { text: t('ACCEPT.CONFIRM_YES'), onPress: () => void doIt() },
    ]);
  };

  if (isPending) return <ActivityIndicator color={colors.primary} style={{ marginTop: 80 }} />;
  if (!manifest) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader title={t('ACCEPT.TITLE')} />
        <Text style={styles.empty}>{t('ACCEPT.NOT_FOUND')}</Text>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <ScreenHeader title={t('ACCEPT.TITLE')} />
      <Text style={styles.sub}>{`${manifest.code} · ${manifest.companyName}`}</Text>
      <Text style={styles.hint}>{t('ACCEPT.HINT')}</Text>
      <FlatList
        data={shipments}
        keyExtractor={(s) => s.id}
        keyboardShouldPersistTaps="handled"
        renderItem={({ item }) => {
          const d = declines[item.id];
          return (
            <View style={[styles.row, d && styles.rowDeclined]}>
              <View style={styles.rowHead}>
                <View style={styles.flex}>
                  <Text style={styles.name}>{item.consigneeName ?? item.orderNumber}</Text>
                  <Text style={styles.addr}>{[item.address, item.city].filter(Boolean).join(', ')}</Text>
                  <Text style={styles.addr}>{`${item.orderNumber} · ${t('COMMON.PIECES')}: ${item.pieces ?? ''}`}</Text>
                </View>
                <View style={styles.switchBox}>
                  <Text style={styles.switchLabel}>{t('ACCEPT.DECLINE')}</Text>
                  <Switch value={Boolean(d)} onValueChange={(on) => setDecline(item.id, on)} />
                </View>
              </View>
              {d && (
                <View style={styles.declineBox}>
                  <OptionSelect
                    options={options}
                    value={d.reasonIndex}
                    placeholder={t('ACCEPT.SELECT_REASON')}
                    onSelect={(i) => setDeclines((prev) => ({ ...prev, [item.id]: { ...d, reasonIndex: i } }))}
                    onClear={() => setDeclines((prev) => ({ ...prev, [item.id]: { ...d, reasonIndex: null } }))}
                  />
                  <TextInput
                    style={styles.input}
                    value={d.note}
                    onChangeText={(note) => setDeclines((prev) => ({ ...prev, [item.id]: { ...d, note } }))}
                    placeholder={t('ACCEPT.NOTE')}
                    maxLength={300}
                  />
                </View>
              )}
            </View>
          );
        }}
      />
      <View style={styles.footer}>
        {busy ? (
          <ActivityIndicator color={colors.primary} />
        ) : (
          <Pressable style={styles.button} onPress={() => void submit()} accessibilityRole="button">
            <Text style={styles.buttonLabel}>{t('ACCEPT.SUBMIT')}</Text>
          </Pressable>
        )}
      </View>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  sub: { textAlign: 'center', fontWeight: '700', color: colors.text, fontSize: 16 },
  hint: { textAlign: 'center', color: colors.muted, marginHorizontal: 24, marginVertical: 8, fontSize: 13 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginHorizontal: 20, marginVertical: 8, padding: 14, gap: 10 },
  rowDeclined: { borderColor: colors.danger },
  rowHead: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  name: { fontWeight: '600', fontSize: 15, color: colors.text },
  addr: { fontSize: 13, color: colors.muted },
  switchBox: { alignItems: 'center' },
  switchLabel: { fontSize: 11, color: colors.muted },
  declineBox: { gap: 8 },
  input: { height: 40, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, fontSize: 15 },
  footer: { padding: 16, alignItems: 'center' },
  button: { backgroundColor: colors.primary, borderRadius: 8, paddingVertical: 12, paddingHorizontal: 28, minWidth: 220, alignItems: 'center' },
  buttonLabel: { color: colors.primaryContrast, fontWeight: '700', fontSize: 17 },
});
