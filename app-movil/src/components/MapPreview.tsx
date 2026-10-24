import { Linking, StyleSheet, View } from 'react-native';
import { WebView } from 'react-native-webview';

// Native map preview: Leaflet + OpenStreetMap tiles inside a WebView. Free, no
// API key, works in Expo Go (react-native-webview is bundled in Expo Go).
// Needs internet to load tiles/Leaflet (a map always does); the rest of the
// detail still renders offline from cache.
function leafletHtml(lat: number, lng: number): string {
  return `<!DOCTYPE html>
<html>
<head>
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
  <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
  <style>html,body,#map{height:100%;margin:0;padding:0;background:#f9f5ed}</style>
</head>
<body>
  <div id="map"></div>
  <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
  <script>
    var lat = ${lat}, lng = ${lng};
    var map = L.map('map', { zoomControl: false, attributionControl: true }).setView([lat, lng], 16);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap'
    }).addTo(map);
    var marker = L.marker([lat, lng]).addTo(map);
    marker.on('click', function () {
      if (window.ReactNativeWebView) window.ReactNativeWebView.postMessage('open-location');
    });
  </script>
</body>
</html>`;
}

export default function MapPreview({ latitude, longitude }: { latitude: number; longitude: number }) {
  // Tapping the marker opens the location outside the app. The https URL lets
  // Android offer the default handler (browser or Maps app).
  const openLocation = () => {
    void Linking.openURL(`https://www.google.com/maps/search/?api=1&query=${latitude},${longitude}`);
  };

  return (
    <View style={styles.fill}>
      <WebView
        originWhitelist={['*']}
        source={{ html: leafletHtml(latitude, longitude) }}
        style={styles.fill}
        scrollEnabled={false}
        javaScriptEnabled
        domStorageEnabled
        onMessage={(e) => {
          if (e.nativeEvent.data === 'open-location') openLocation();
        }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { width: '100%', height: '100%' },
});
