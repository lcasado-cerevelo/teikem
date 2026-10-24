// Doble de expo-speech-recognition para las pruebas (jest no tiene el módulo nativo): sin reconocimiento disponible, así que el micrófono no se ofrece.
export const ExpoSpeechRecognitionModule = {
  isRecognitionAvailable: () => false,
  requestPermissionsAsync: async () => ({ granted: false }),
  start: () => undefined,
  stop: () => undefined,
  abort: () => undefined,
}
export function useSpeechRecognitionEvent(): void {
  /* sin eventos en las pruebas */
}
