// Acceso tipado a las variables públicas que Expo incrusta al compilar (todas con el prefijo EXPO_PUBLIC_).
//
// Obligatoria en .env:
//   EXPO_PUBLIC_BASE_URL  -> la dirección del API de Teikem, p. ej. https://api.miempresa.com/api/
//
// Los mapas usan OpenStreetMap (Nominatim + Leaflet): no hace falta llave.

function required(name: string, value: string | undefined): string {
  if (!value || value.trim() === '') {
    throw new Error(`Falta la variable ${name}. Agréguela a su .env (con el prefijo EXPO_PUBLIC_).`);
  }
  return value.trim();
}

export const env = {
  baseUrl: required('EXPO_PUBLIC_BASE_URL', process.env.EXPO_PUBLIC_BASE_URL),
};
