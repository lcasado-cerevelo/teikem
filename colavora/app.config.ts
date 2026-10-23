import { ExpoConfig, ConfigContext } from 'expo/config';

// ---------------------------------------------------------------------------
// Multi-brand (white-label) config: ONE codebase, several installable apps —
// each with its own name/icon/splash/package — selected via the APP_VARIANT
// env var. Assets live in ./assets/brands/<variant>/. To add a company: drop
// its assets in a new ./assets/brands/<key>/ folder, add an entry to BRANDS
// below, and add matching profiles in eas.json (see the delapaz-* profiles).
// ---------------------------------------------------------------------------
type BrandKey = 'advance' | 'delapaz';

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
export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: brand.name,
  slug: 'colavora',
  scheme: brand.scheme,
  owner: 'lcasado-advance',
  version: '1.3.0',
  orientation: 'portrait',
  icon: brand.icon,
  userInterfaceStyle: 'automatic',
  newArchEnabled: true,
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
    // Play Store exige un versionCode entero, único y creciente por release.
    // Se sube A MANO junto con `version` en cada release (autoIncrement de EAS
    // no funciona con config dinámico como este archivo).
    versionCode: 4,
    adaptiveIcon: {
      foregroundImage: brand.adaptiveIcon,
      backgroundColor: '#FFFFFF',
    },
    permissions: [
      'ACCESS_FINE_LOCATION',
      'ACCESS_COARSE_LOCATION',
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
    [
      'expo-location',
      {
        locationWhenInUsePermission: `${brand.name} uses your location to geotag deliveries.`,
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
    brand: { name: brand.name, primaryColor: brand.primaryColor },
    eas: {
      projectId: 'bcce9813-d49e-4447-b265-c60dc296a26c',
    },
  },
});
