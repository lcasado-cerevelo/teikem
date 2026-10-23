import React, { createContext, useContext, useEffect, useMemo, useState } from 'react';
import { StatusBar, StyleSheet, useColorScheme } from 'react-native';
import { prefsStorage } from '@services/prefsStorage';
import { darkColors, DEFAULT_THEME_MODE, lightColors, type Colors, type ThemeMode } from '@constants/theme';

const KEY = 'themeMode';

interface ThemeValue {
  /** Lo que la persona escogió (Automático por omisión). */
  mode: ThemeMode;
  /** Lo que se está viendo ahora (en Automático, lo que dice el teléfono). */
  scheme: 'light' | 'dark';
  colors: Colors;
  setMode: (mode: ThemeMode) => void;
}

const ThemeContext = createContext<ThemeValue | undefined>(undefined);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const system = useColorScheme();
  const [mode, setModeState] = useState<ThemeMode>(DEFAULT_THEME_MODE);

  useEffect(() => {
    void prefsStorage.get(KEY).then((v) => {
      if (v === 'auto' || v === 'light' || v === 'dark') setModeState(v);
    });
  }, []);

  const value = useMemo<ThemeValue>(() => {
    const scheme = mode === 'auto' ? (system === 'dark' ? 'dark' : 'light') : mode;
    return {
      mode,
      scheme,
      colors: scheme === 'dark' ? darkColors : lightColors,
      setMode: (m) => {
        setModeState(m);
        void prefsStorage.set(KEY, m);
      },
    };
  }, [mode, system]);

  return (
    <ThemeContext.Provider value={value}>
      <StatusBar barStyle={value.scheme === 'dark' ? 'light-content' : 'dark-content'} backgroundColor={value.colors.background} />
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used within a ThemeProvider');
  return ctx;
}

/** Estilos que dependen de la paleta: se rehacen solo cuando cambia el tema. */
export function useStyles<T extends StyleSheet.NamedStyles<T> | StyleSheet.NamedStyles<any>>(factory: (colors: Colors) => T): T {
  const { colors } = useTheme();
  return useMemo(() => factory(colors), [factory, colors]);
}
