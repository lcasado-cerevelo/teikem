// Centralized, typed access to the public env vars Expo inlines at build time.
// All must be prefixed EXPO_PUBLIC_ to be available on device and web.
//
// Required in .env:
//   EXPO_PUBLIC_BASE_URL          -> https://api.your-host.com/api/
//   EXPO_PUBLIC_AUTH0_DOMAIN      -> your-tenant.us.auth0.com
//   EXPO_PUBLIC_AUTH0_CLIENT_ID   -> the native client id
//
// Maps use OpenStreetMap (Nominatim + Leaflet/OSM tiles) — no API key needed.

function required(name: string, value: string | undefined): string {
  if (!value || value.trim() === '') {
    throw new Error(`Missing required env var: ${name}. Add it to your .env (EXPO_PUBLIC_ prefix).`);
  }
  return value.trim();
}

export const env = {
  baseUrl: required('EXPO_PUBLIC_BASE_URL', process.env.EXPO_PUBLIC_BASE_URL),
  auth0Domain: required('EXPO_PUBLIC_AUTH0_DOMAIN', process.env.EXPO_PUBLIC_AUTH0_DOMAIN),
  auth0ClientId: required('EXPO_PUBLIC_AUTH0_CLIENT_ID', process.env.EXPO_PUBLIC_AUTH0_CLIENT_ID),
};

export const auth0Audience = `https://${env.auth0Domain}/api/v2/`;
