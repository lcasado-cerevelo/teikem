import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import * as SecureStore from 'expo-secure-store';
import en from './en.json';
import es from './es.json';

const LANG_KEY = 'app_language';
export type AppLanguage = 'es' | 'en';

void i18next.use(initReactI18next).init({
  resources: { en, es },
  lng: 'es',
  fallbackLng: 'es',
  interpolation: { escapeValue: false },
});

// Restaura el idioma guardado (async; mientras tanto queda en 'es').
SecureStore.getItemAsync(LANG_KEY)
  .then((saved) => {
    if ((saved === 'es' || saved === 'en') && saved !== i18next.language) {
      void i18next.changeLanguage(saved);
    }
  })
  .catch(() => {});

export async function setLanguage(lng: AppLanguage) {
  await i18next.changeLanguage(lng);
  try {
    await SecureStore.setItemAsync(LANG_KEY, lng);
  } catch {
    /* no-op */
  }
}

export default i18next;
