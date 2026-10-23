import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { acceptLegal, fetchLegalDocument, type LegalDocumentDto } from '@services/auth/teikemAuth';
import { colors } from '@constants/theme';

/** ¿Llegó el lector al final del texto? (margen de unos píxeles; un texto que cabe sin desplazar cuenta como leído). */
export function reachedEnd(m: { contentOffset: { y: number }; layoutMeasurement: { height: number }; contentSize: { height: number } }, margin = 24): boolean {
  return m.contentOffset.y + m.layoutMeasurement.height >= m.contentSize.height - margin;
}

// Política de privacidad y términos de uso: un solo documento; «Acepto» se habilita solo al llegar al final. Se pide el primer ingreso y cada vez que cambia el documento.
export default function Legal() {
  const { t, i18n } = useTranslation();
  const { accessToken, legalAccepted, logout } = useAuth();
  const lang = i18n.language === 'en' ? 'en' : 'es';
  const [doc, setDoc] = useState<LegalDocumentDto | null>(null);
  const [error, setError] = useState(false);
  const [read, setRead] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const token = accessToken();
    if (!token) return;
    let alive = true;
    fetchLegalDocument(token, lang)
      .then((d) => alive && setDoc(d))
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [accessToken, lang]);

  const accept = async () => {
    const token = accessToken();
    if (!token || !doc?.version) return;
    setBusy(true);
    try {
      await acceptLegal(token, doc.version, lang);
      legalAccepted();
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  };

  let lastPart = '';
  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <Text style={styles.title}>{doc?.title ?? t('LEGAL.TITLE')}</Text>
      <Text style={styles.lead}>{t('LEGAL.LEAD')}</Text>
      {error && <Text style={styles.error}>{t('LEGAL.ERROR')}</Text>}
      {!doc && !error ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        !!doc && (
          <ScrollView
            style={styles.scroll}
            contentContainerStyle={styles.scrollContent}
            scrollEventThrottle={64}
            onScroll={(e) => reachedEnd(e.nativeEvent) && setRead(true)}
            onContentSizeChange={(_w, h) => h < 300 && setRead(true)}
          >
            <Text style={styles.intro}>{doc.intro}</Text>
            <Text style={styles.version}>{doc.version}</Text>
            {(doc.sections ?? []).map((s) => {
              const heading = s.part !== lastPart ? (s.part === 'privacy' ? doc.partPrivacy : doc.partTerms) : null;
              lastPart = s.part ?? '';
              return (
                <View key={s.id} style={styles.section}>
                  {!!heading && <Text style={styles.part}>{heading}</Text>}
                  <Text style={styles.heading}>{s.heading}</Text>
                  {(s.paragraphs ?? []).map((p, i) => (
                    <Text key={i} style={styles.paragraph}>
                      {p}
                    </Text>
                  ))}
                </View>
              );
            })}
          </ScrollView>
        )
      )}
      {!read && !!doc && <Text style={styles.lead}>{t('LEGAL.SCROLL_HINT')}</Text>}
      <Pressable style={[styles.accept, (!read || !doc || busy) && styles.disabled]} disabled={!read || !doc || busy} onPress={() => void accept()} accessibilityRole="button">
        {busy ? <ActivityIndicator color={colors.primaryContrast} /> : <Text style={styles.acceptText}>{t('LEGAL.ACCEPT')}</Text>}
      </Pressable>
      <Pressable style={styles.decline} onPress={() => void logout()} accessibilityRole="button">
        <Text style={styles.declineText}>{t('LEGAL.DECLINE')}</Text>
      </Pressable>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, padding: 16, gap: 10 },
  title: { fontSize: 20, fontWeight: '700', color: colors.primary },
  lead: { fontSize: 13, color: colors.muted },
  error: { color: colors.danger },
  scroll: { flex: 1, borderWidth: 1, borderColor: colors.gray, borderRadius: 12 },
  scrollContent: { padding: 14, gap: 6 },
  intro: { fontWeight: '700', color: colors.text },
  version: { fontSize: 12, color: colors.muted },
  section: { gap: 6 },
  part: { fontSize: 18, fontWeight: '800', marginTop: 16, color: colors.primary },
  heading: { fontSize: 15, fontWeight: '700', marginTop: 8, color: colors.text },
  paragraph: { fontSize: 14, lineHeight: 21, color: colors.text },
  accept: { backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 14, alignItems: 'center' },
  acceptText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 16 },
  disabled: { opacity: 0.5 },
  decline: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, paddingVertical: 12, alignItems: 'center' },
  declineText: { color: colors.text, fontWeight: '600' },
});
