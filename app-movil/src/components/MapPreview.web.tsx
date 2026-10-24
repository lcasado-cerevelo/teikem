import React from 'react';

// Web map preview: OpenStreetMap's embed iframe (free, no key). This .web file
// runs under react-dom, so a raw <iframe> is valid here. Keeps react-native-webview
// out of the web bundle.
export default function MapPreview({ latitude, longitude }: { latitude: number; longitude: number }) {
  const d = 0.004;
  const bbox = `${longitude - d},${latitude - d},${longitude + d},${latitude + d}`;
  const src =
    `https://www.openstreetmap.org/export/embed.html` +
    `?bbox=${bbox}&layer=mapnik&marker=${latitude},${longitude}`;

  return React.createElement('iframe', {
    src,
    title: 'map',
    style: { border: 0, width: '100%', height: '100%', minHeight: 280 },
  });
}
