import Constants from 'expo-constants';

// Mirrors the BRANDS map in app.config.ts. That file picks the variant at
// BUILD time (icon/name/package); this one lets in-app JS (Login screen logo,
// "Powered by" text) render the matching brand at RUNTIME. Metro can't
// `require()` a dynamic path, so every brand's assets are required statically
// and picked by key.
export type AppVariant = 'advance' | 'delapaz';

interface BrandInfo {
  name: string;
  poweredBy: string;
  logo: number;
  primaryColor: string;
}

const BRANDS: Record<AppVariant, BrandInfo> = {
  advance: {
    name: 'Advance App',
    poweredBy: 'Powered by Cerevelo',
    logo: require('@assets/brands/advance/login-logo.png'),
    primaryColor: '#F1592A',
  },
  delapaz: {
    name: 'De La Paz App',
    poweredBy: 'Powered by Cerevelo',
    logo: require('@assets/brands/delapaz/login-logo.png'),
    primaryColor: '#EC0C8C',
  },
};

export const APP_VARIANT: AppVariant =
  (Constants.expoConfig?.extra?.appVariant as AppVariant) ?? 'advance';

export const BRAND: BrandInfo = BRANDS[APP_VARIANT] ?? BRANDS.advance;
