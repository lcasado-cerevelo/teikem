import * as Location from 'expo-location';

export interface Fix {
  lat: number;
  lng: number;
}

// La ubicación del teléfono AL MOMENTO de entregar o marcar «no entregado». Se pide aparte del seguimiento de la ruta: aunque el chofer lo haya apagado en Perfil,
// la entrega y la excepción SIEMPRE llevan el GPS (la oficina lo compara con la dirección; el chofer puede estar en su casa). Si no hay permiso o señal, devuelve null y el registro sigue sin GPS.
export async function getCurrentFix(timeoutMs = 10_000): Promise<Fix | null> {
  try {
    let perm = await Location.getForegroundPermissionsAsync();
    if (!perm.granted) perm = await Location.requestForegroundPermissionsAsync();
    if (!perm.granted) return null;
    const pos = await Promise.race([
      Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.High }),
      new Promise<null>((resolve) => setTimeout(() => resolve(null), timeoutMs)),
    ]);
    const p = pos ?? (await Location.getLastKnownPositionAsync({ maxAge: 5 * 60_000 }));
    return p ? { lat: p.coords.latitude, lng: p.coords.longitude } : null;
  } catch {
    return null;
  }
}
