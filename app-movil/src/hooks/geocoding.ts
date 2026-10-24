import { useEffect, useState } from 'react';
import { defaultLocation } from '@constants/index';

interface Args {
  storedLat?: number;
  storedLng?: number;
  addressLine1?: string | null;
  zip?: string | null;
  city?: string | null;
}

const hasCoords = (lat?: number, lng?: number): boolean =>
  Number.isFinite(lat) && Number.isFinite(lng) && !(lat === 0 && lng === 0);

// Resolves the delivery point for the map, in priority order:
//   1) the consignee's STORED latitude/longitude (if present),
//   2) geocode "addressLine1 + zip",
//   3) geocode "addressLine1 + city".
// `located` is true only when a point was resolved; otherwise the UI shows a
// "couldn't place the address" message instead of a wrong pin.
export function useCoordinatesFromAddress({ storedLat, storedLng, addressLine1, zip, city }: Args) {
  const [location, setLocation] = useState<{ lat: number; lng: number }>(defaultLocation);
  const [loading, setLoading] = useState(false);
  const [located, setLocated] = useState(false);

  const line1 = (addressLine1 ?? '').trim();
  const zipS = (zip ?? '').trim();
  const cityS = (city ?? '').trim();

  useEffect(() => {
    // 1) Stored coordinates win — no network needed.
    if (hasCoords(storedLat, storedLng)) {
      setLocation({ lat: storedLat as number, lng: storedLng as number });
      setLocated(true);
      setLoading(false);
      return;
    }

    if (!line1) {
      setLocated(false);
      return;
    }

    // 2) and 3): try "line1 + zip", then "line1 + city".
    const queries = [`${line1} ${zipS}`.trim(), `${line1} ${cityS}`.trim()].filter(
      (q, i, arr) => q && arr.indexOf(q) === i,
    );

    let cancelled = false;
    (async () => {
      setLoading(true);
      const geocode = async (q: string): Promise<{ lat: number; lng: number } | null> => {
        try {
          const url = `https://nominatim.openstreetmap.org/search?format=json&limit=1&q=${encodeURIComponent(
            `${q} Puerto Rico`,
          )}`;
          const res = await fetch(url, {
            headers: { 'User-Agent': 'ColavoraDriver/1.0 (delivery app)', Accept: 'application/json' },
          });
          const json = (await res.json()) as Array<{ lat: string; lon: string }>;
          return json?.[0] ? { lat: parseFloat(json[0].lat), lng: parseFloat(json[0].lon) } : null;
        } catch {
          return null;
        }
      };

      let found: { lat: number; lng: number } | null = null;
      for (const q of queries) {
        found = await geocode(q);
        if (cancelled) return;
        if (found) break;
      }
      if (cancelled) return;
      if (found) {
        setLocation(found);
        setLocated(true);
      } else {
        setLocated(false);
      }
      setLoading(false);
    })();

    return () => {
      cancelled = true;
    };
  }, [storedLat, storedLng, line1, zipS, cityS]);

  return { location, loading, located };
}
