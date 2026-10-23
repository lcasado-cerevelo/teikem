import { useCallback } from 'react';
import { Alert } from 'react-native';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { authenticate, getBiometricEnabled, isBiometricAvailable } from '@services/auth/biometricAuth';

// Después de entrar (con la contraseña y, si se pidió, el código) se ofrece «Abrir con huella» una sola vez por teléfono mientras no esté activo.
// Aceptar exige PRIMERO pasar la huella del teléfono: así se comprueba que quien lo activa puede abrirlo.
export function useBiometricOffer(): () => Promise<void> {
  const { t } = useTranslation();
  const { setBiometricEnabled } = useAuth();
  return useCallback(async () => {
    if (!(await isBiometricAvailable())) return;
    if (await getBiometricEnabled()) return;
    Alert.alert(t('LOGIN.BIOMETRIC_OFFER_TITLE'), t('LOGIN.BIOMETRIC_OFFER_BODY'), [
      { text: t('LOGIN.BIOMETRIC_NOT_NOW'), style: 'cancel' },
      {
        text: t('LOGIN.BIOMETRIC_ENABLE'),
        onPress: () => {
          void (async () => {
            if (await authenticate(t('LOGIN.BIOMETRIC_ENABLE_PROMPT'), t('LOGIN.BIOMETRIC_CANCEL'))) await setBiometricEnabled(true);
          })();
        },
      },
    ]);
  }, [setBiometricEnabled, t]);
}
