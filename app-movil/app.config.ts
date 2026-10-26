import { ExpoConfig, ConfigContext } from 'expo/config';

// ---------------------------------------------------------------------------
// Multi-brand (white-label) config: ONE codebase, several installable apps —
// each with its own name/icon/splash/package — selected via the APP_VARIANT
// env var. Assets live in ./assets/brands/<variant>/. To add a company: drop
// its assets in a new ./assets/brands/<key>/ folder, add an entry to BRANDS
// below, and add matching profiles in eas.json (see the delapaz-* profiles).
// ---------------------------------------------------------------------------
type BrandKey = 'advance' | 'delapaz' | 'advance-test';

interface Brand {
  name: string;
  scheme: string;
  androidPackage: string;
  iosBundleId: string;
  icon: string;
  adaptiveIcon: string;
  splash: string;
  favicon: string;
  primaryColor: string;
}

const BRANDS: Record<BrandKey, Brand> = {
  advance: {
    name: 'Advance App',
    scheme: 'advanceapp',
    androidPackage: 'com.advancelogisticspr.app',
    iosBundleId: 'com.advancelogisticspr.app',
    icon: './assets/brands/advance/icon.png',
    adaptiveIcon: './assets/brands/advance/adaptive-icon.png',
    splash: './assets/brands/advance/splash.png',
    favicon: './assets/brands/advance/favicon.png',
    primaryColor: '#F1592A',
  },
  delapaz: {
    name: 'De La Paz App',
    scheme: 'delapazapp',
    androidPackage: 'com.delapaztransport.app',
    iosBundleId: 'com.delapaztransport.app',
    icon: './assets/brands/delapaz/icon.png',
    adaptiveIcon: './assets/brands/delapaz/adaptive-icon.png',
    splash: './assets/brands/delapaz/splash.png',
    favicon: './assets/brands/delapaz/favicon.png',
    primaryColor: '#EC0C8C',
  },
  // Ambiente de TEST (test.advancelogisticspr.com): se instala AL LADO de la app de producción (otro paquete) y dice TEST en el nombre.
  // Lo compila scripts/construir-apk.ps1 -App movil -Test. Sin la app Android de este paquete en google-services.json, queda sin avisos emergentes.
  'advance-test': {
    name: 'Advance App TEST',
    scheme: 'advanceapptest',
    androidPackage: 'com.advancelogisticspr.app.test',
    iosBundleId: 'com.advancelogisticspr.app.test',
    icon: './assets/brands/advance/icon.png',
    adaptiveIcon: './assets/brands/advance/adaptive-icon.png',
    splash: './assets/brands/advance/splash.png',
    favicon: './assets/brands/advance/favicon.png',
    primaryColor: '#F1592A',
  },
};

// APP_VARIANT is set per EAS build profile (see eas.json). Local `expo start`
// has no profile, so it defaults to 'advance' — override with
// `$env:APP_VARIANT='delapaz'; npx expo start -c` (PowerShell) to preview the
// other brand locally.
const VARIANT = (process.env.APP_VARIANT as BrandKey | undefined) ?? 'advance';
const brand = BRANDS[VARIANT] ?? BRANDS.advance;

// NOTE: the URL scheme below is used as the OAuth redirect for Universal Login
// (e.g. advanceapp://). Register it in the Auth0 application's Allowed
// Callback URLs — though today's login is a direct password grant with no
// redirect, so this isn't currently exercised.
// eslint-disable-next-line @typescript-eslint/no-require-imports
const { existsSync, readFileSync } = require('fs') as { existsSync: (p: string) => boolean; readFileSync: (p: string, enc: string) => string };

// ¿google-services.json trae la app Android de esta marca? (si no, esa marca se construye sin avisos emergentes en vez de fallar)
function googleServicesHas(pkg: string): boolean {
  try {
    if (!existsSync('./google-services.json')) return false;
    const j = JSON.parse(readFileSync('./google-services.json', 'utf8')) as { client?: Array<{ client_info?: { android_client_info?: { package_name?: string } } }> };
    return (j.client ?? []).some((c) => c.client_info?.android_client_info?.package_name === pkg);
  } catch {
    return false;
  }
}

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: brand.name,
  slug: 'colavora',
  scheme: brand.scheme,
  owner: 'lcasado-advance',
  version: process.env.TEIKEM_APP_VERSION || '1.3.0',   // scripts/construir-apk.ps1 la fija; sin ella, la de aquí
  orientation: 'portrait',
  icon: brand.icon,
  userInterfaceStyle: 'automatic',
  ios: {
    supportsTablet: true,
    bundleIdentifier: brand.iosBundleId,
    infoPlist: {
      NSLocationWhenInUseUsageDescription:
        `${brand.name} uses your location to geotag deliveries and show shipments on the map.`,
      NSCameraUsageDescription:
        `${brand.name} uses the camera to photograph proof of delivery.`,
      NSPhotoLibraryUsageDescription:
        `${brand.name} attaches photos as proof of delivery.`,
    },
  },
  android: {
    package: brand.androidPackage,
    // Firebase (avisos emergentes). El archivo trae la app Android de cada marca; no es secreto (va dentro de la app).
    ...(googleServicesHas(brand.androidPackage) ? { googleServicesFile: './google-services.json' } : {}),
    // Play Store exige un versionCode entero, único y creciente por release.
    // Se sube A MANO junto con `version` en cada release (autoIncrement de EAS
    // no funciona con config dinámico como este archivo).
    versionCode: Number.parseInt(process.env.TEIKEM_VERSION_CODE ?? '', 10) || 4,   // scripts/construir-apk.ps1 lo sube solo; sin él, el de aquí
    adaptiveIcon: {
      foregroundImage: brand.adaptiveIcon,
      backgroundColor: '#FFFFFF',
    },
    permissions: [
      'ACCESS_FINE_LOCATION',
      'ACCESS_COARSE_LOCATION',
      'ACCESS_BACKGROUND_LOCATION',
      'FOREGROUND_SERVICE',
      'FOREGROUND_SERVICE_LOCATION',
      'ACCESS_NETWORK_STATE',
      'CAMERA',
    ],
  },
  web: {
    bundler: 'metro',
    output: 'single',
    favicon: brand.favicon,
  },
  plugins: [
    'expo-router',
    // APK firmado con la llave propia (TEIKEM_RELEASE_KEYSTORE_*), igual que las apps del almacén y del cliente; sin las variables cae a la llave de depuración
    './plugins/withAndroidReleaseSigning',
    [
      'expo-splash-screen',
      {
        image: brand.splash,
        resizeMode: 'contain',
        backgroundColor: '#FFFFFF',
      },
    ],
    'expo-secure-store',
    [
      'expo-local-authentication',
      {
        faceIDPermission: `${brand.name} uses Face ID to sign you in quickly.`,
      },
    ],
    'expo-web-browser',
    ['expo-notifications', { color: '#03204B' }],
    [
      'expo-location',
      {
        locationWhenInUsePermission: `${brand.name} uses your location to geotag deliveries.`,
        locationAlwaysAndWhenInUsePermission: `${brand.name} records your route while a route is in progress.`,
        isAndroidBackgroundLocationEnabled: true,
        isAndroidForegroundServiceEnabled: true,
      },
    ],
    [
      'expo-camera',
      {
        cameraPermission: `${brand.name} uses the camera to photograph proof of delivery.`,
      },
    ],
  ],
  experiments: {
    typedRoutes: true,
  },
  extra: {
    ...config.extra,
    appVariant: VARIANT,
    buildStamp: process.env.TEIKEM_BUILD_STAMP ?? 'local',
    brand: { name: brand.name, primaryColor: brand.primaryColor },
    eas: {
      projectId: 'bcce9813-d49e-4447-b265-c60dc296a26c',
    },
  },
});
