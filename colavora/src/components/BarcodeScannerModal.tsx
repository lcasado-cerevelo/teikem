import { useEffect, useRef, useState } from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { CameraView, useCameraPermissions, type BarcodeScanningResult } from 'expo-camera';
import { MaterialIcons } from '@expo/vector-icons';
import { colors } from '@constants/theme';

// Tipos que realmente circulan en las piezas de Colavora: los códigos van desde
// SSCC de 20 dígitos ("00003166348830670014") hasta alfanuméricos cortos
// ("120500485L"), así que se cubre el abanico de 1D y de paso QR/DataMatrix por
// si alguna etiqueta los trae.
const BARCODE_TYPES = [
  'code128',
  'code39',
  'code93',
  'codabar',
  'ean13',
  'ean8',
  'itf14',
  'upc_a',
  'upc_e',
  'qr',
  'pdf417',
  'datamatrix',
] as const;

// Escáner para el buscador del listado. Devuelve el texto leído y cierra.
export default function BarcodeScannerModal({
  visible,
  onClose,
  onScanned,
}: {
  visible: boolean;
  onClose: () => void;
  onScanned: (value: string) => void;
}) {
  const { t } = useTranslation();
  const [permission, requestPermission] = useCameraPermissions();
  // onBarcodeScanned se dispara EN CADA FRAME mientras el código esté a la
  // vista; sin este cerrojo el modal intentaría cerrarse decenas de veces y el
  // texto de búsqueda parpadearía.
  const locked = useRef(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (visible) {
      locked.current = false;
      setError(null);
    }
  }, [visible]);

  // Pedir el permiso al abrir, no al montar: así el chofer solo ve el diálogo
  // del sistema si de verdad va a escanear.
  useEffect(() => {
    if (visible && permission && !permission.granted && permission.canAskAgain) {
      void requestPermission();
    }
  }, [visible, permission, requestPermission]);

  const handleScan = (result: BarcodeScanningResult) => {
    if (locked.current) return;
    const value = (result?.data ?? '').trim();
    if (!value) return;
    locked.current = true;
    onScanned(value);
    onClose();
  };

  const denied = permission && !permission.granted && !permission.canAskAgain;

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={styles.screen}>
        <View style={styles.header}>
          <Text style={styles.title}>{t('ORDERS.SCAN_TITLE')}</Text>
          <Pressable onPress={onClose} hitSlop={10} accessibilityLabel={t('ORDERS.SCAN_CANCEL')}>
            <MaterialIcons name="close" size={26} color="#fff" />
          </Pressable>
        </View>

        {denied ? (
          <View style={styles.center}>
            <Text style={styles.message}>{t('DELIVERY.CAMERA_PERMISSION')}</Text>
          </View>
        ) : !permission?.granted ? (
          <View style={styles.center}>
            <Text style={styles.message}>{t('ORDERS.SCAN_PERMISSION_WAIT')}</Text>
          </View>
        ) : (
          <>
            <CameraView
              style={styles.camera}
              facing="back"
              barcodeScannerSettings={{ barcodeTypes: [...BARCODE_TYPES] }}
              onBarcodeScanned={handleScan}
            />
            {/* Marco guía: sin una referencia visual la gente no sabe dónde
                apuntar y cree que el escáner no sirve. */}
            <View pointerEvents="none" style={styles.overlay}>
              <View style={styles.frame} />
              <Text style={styles.hint}>{t('ORDERS.SCAN_HINT')}</Text>
            </View>
          </>
        )}

        {error && <Text style={styles.error}>{error}</Text>}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: '#000' },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 20,
    paddingTop: 48,
    paddingBottom: 12,
  },
  title: { color: '#fff', fontSize: 17, fontWeight: '700' },
  camera: { flex: 1 },
  // Posición explícita en vez de StyleSheet.absoluteFillObject: los tipos de
  // esta versión de RN ya no lo exponen.
  overlay: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 16,
  },
  frame: {
    width: '78%',
    height: 150,
    borderWidth: 2,
    borderColor: '#fff',
    borderRadius: 12,
    backgroundColor: 'transparent',
  },
  hint: { color: '#fff', fontSize: 14, textAlign: 'center', paddingHorizontal: 30 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 30 },
  message: { color: '#fff', textAlign: 'center', fontSize: 15 },
  error: { color: colors.danger, textAlign: 'center', padding: 12 },
});
