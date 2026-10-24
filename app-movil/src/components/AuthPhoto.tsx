import { useState } from 'react';
import { Image, StyleSheet, View, type ImageStyle, type StyleProp } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { photoSource, type MessagePhoto } from '@services/messagesApi';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// La foto de un mensaje, pedida al servidor con la firma de la compañía. No se guarda en el teléfono: se ve en pantalla (el sistema puede dejarla en su caché temporal).
// Si ya se borró o archivó, o no se puede pedir, se muestra un aviso en vez de la imagen.
export default function AuthPhoto({ tenantId, photo, thumb, style }: { tenantId: number; photo: MessagePhoto; thumb: boolean; style?: StyleProp<ImageStyle> }) {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [failed, setFailed] = useState(false);
  if (photo.state !== 'ACTIVE' || failed) {
    const label = photo.state === 'DELETED' ? t('CHAT.PHOTO_DELETED') : photo.state === 'ARCHIVED' ? t('CHAT.PHOTO_ARCHIVED') : t('CHAT.PHOTO_UNAVAILABLE');
    return (
      <View style={[styles.gone, style as object]}>
        <Text style={styles.goneText}>{label}</Text>
      </View>
    );
  }
  return <Image source={photoSource(tenantId, photo.publicId, thumb && photo.hasThumb)} style={style} resizeMode={thumb ? 'cover' : 'contain'} onError={() => setFailed(true)} />;
}

const createStyles = (colors: Colors) => StyleSheet.create({
  gone: { alignItems: 'center', justifyContent: 'center', backgroundColor: colors.backgroundSecondary, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, padding: 8 },
  goneText: { color: colors.muted, fontSize: 12, textAlign: 'center' },
});
