// Amplía los tipos de expo-sqlite y expo-secure-store con las funciones que solo existen en sus mocks de Jest
// (__mocks__/expo-sqlite.ts, __mocks__/expo-secure-store.ts), para que las pruebas puedan importarlas sin `any`.
declare module 'expo-sqlite' {
  export function __resetAllForTests(): void
}

declare module 'expo-secure-store' {
  export function __resetSecureStoreForTests(): void
}

export {}
