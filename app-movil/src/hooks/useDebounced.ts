import { useEffect, useState } from 'react';

/** El texto ya asentado: cambia medio segundo después de dejar de teclear (para no buscar en cada letra). */
export function useDebounced(value: string, ms = 500): string {
  const [v, setV] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}
