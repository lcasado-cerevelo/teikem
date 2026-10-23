import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

// Preferencias del teléfono que NO se borran al cerrar sesión (tema…): son del aparato, no de la persona.
const KEY = 'teikem.choferes.prefs';

type Prefs = Record<string, string>;

async function read(): Promise<Prefs> {
  try {
    const raw = Platform.OS === 'web' ? localStorage.getItem(KEY) : await SecureStore.getItemAsync(KEY);
    return raw ? (JSON.parse(raw) as Prefs) : {};
  } catch {
    return {};
  }
}

export const prefsStorage = {
  async get(key: string): Promise<string | null> {
    return (await read())[key] ?? null;
  },
  async set(key: string, value: string): Promise<void> {
    const next = JSON.stringify({ ...(await read()), [key]: value });
    try {
      if (Platform.OS === 'web') localStorage.setItem(KEY, next);
      else await SecureStore.setItemAsync(KEY, next);
    } catch {
      /* sin almacenamiento: la preferencia vale solo esta vez */
    }
  },
};
