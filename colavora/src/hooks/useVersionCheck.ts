import { useEffect } from 'react';
import { Alert, Linking } from 'react-native';
import Constants from 'expo-constants';
import { useTranslation } from 'react-i18next';
import { onlineManager } from '@tanstack/react-query';
import { api } from '@config/axios';
import { APP_VARIANT } from '@constants/brand';

interface VersionInfo {
  minVersion: string;
  latestVersion: string;
  downloadUrl: string;
}

// true when a < b ("1.0.9" < "1.1.0"). Numeric per-segment compare — a plain
// string compare would say "1.10.0" < "1.9.0".
function isOlder(a: string, b: string): boolean {
  const pa = a.split('.').map((n) => parseInt(n, 10) || 0);
  const pb = b.split('.').map((n) => parseInt(n, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const da = pa[i] ?? 0;
    const db = pb[i] ?? 0;
    if (da !== db) return da < db;
  }
  return false;
}

// Startup update check (sideloaded APKs have no store to do this for them).
// Asks the API which version is current; older than minVersion blocks the app
// until reinstalled, older than latestVersion just offers the download once.
// Silent on any failure — an update nag must never break an offline route.
export function useVersionCheck() {
  const { t } = useTranslation();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!onlineManager.isOnline()) return;
      try {
        const { data } = await api.get<VersionInfo>('common/version', {
          params: { variant: APP_VARIANT },
        });
        if (cancelled || !data?.latestVersion) return;
        const current = Constants.expoConfig?.version ?? '0.0.0';
        const open = () => {
          if (data.downloadUrl) void Linking.openURL(data.downloadUrl);
        };
        if (isOlder(current, data.minVersion)) {
          // Forced: the only button re-opens the download and re-shows the
          // alert, so there is no way into the app on a blocked version.
          const show = () =>
            Alert.alert(t('UPDATE.REQUIRED_TITLE'), t('UPDATE.REQUIRED_BODY'), [
              {
                text: t('UPDATE.UPDATE_NOW'),
                onPress: () => {
                  open();
                  show();
                },
              },
            ], { cancelable: false });
          show();
        } else if (isOlder(current, data.latestVersion)) {
          Alert.alert(
            t('UPDATE.AVAILABLE_TITLE'),
            t('UPDATE.AVAILABLE_BODY', { version: data.latestVersion }),
            [
              { text: t('UPDATE.LATER'), style: 'cancel' },
              { text: t('UPDATE.UPDATE_NOW'), onPress: open },
            ],
          );
        }
      } catch {
        /* offline or endpoint missing: never block the driver */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [t]);
}
