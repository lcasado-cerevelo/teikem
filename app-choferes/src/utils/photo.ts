import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';

// Proof photos travel as base64 INSIDE the dispatch/exception form body. A raw
// camera capture (even at ImagePicker quality 0.4) is a full-resolution JPEG —
// easily 2-4MB, ~5MB as base64 — which on a mobile uplink can blow past the
// HTTP client's timeout and make a perfectly-online save look like "offline,
// queued". A POD photo doesn't need 12MP: cap the width and recompress.
const MAX_WIDTH = 1280;
const JPEG_QUALITY = 0.6;

export interface ProofPhoto {
  uri: string;
  base64: string;
}

// Downscale + recompress a camera capture and return it with base64.
// `originalWidth` (from the ImagePicker asset) avoids upscaling small images.
export async function compressProofPhoto(
  uri: string,
  originalWidth?: number,
): Promise<ProofPhoto> {
  const targetWidth = Math.min(MAX_WIDTH, originalWidth ?? MAX_WIDTH);
  const rendered = await ImageManipulator.manipulate(uri).resize({ width: targetWidth }).renderAsync();
  const saved = await rendered.saveAsync({
    format: SaveFormat.JPEG,
    compress: JPEG_QUALITY,
    base64: true,
  });
  return { uri: saved.uri, base64: saved.base64 ?? '' };
}
