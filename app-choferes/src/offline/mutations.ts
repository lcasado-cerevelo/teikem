import { enqueue } from './outbox';

export interface DispatchInput {
  shipmentId: number;
  // Real piece barcodes. The single-dispatch SP silently no-ops when the barcode
  // doesn't match a Shipments_Pieces row (Shipments.Barcode is a comma-joined
  // list, not a real barcode), so we always dispatch through the batch endpoint,
  // whose SP validates by ShipmentID and reports failures.
  barcodes: string[];
  podName: string;
  latitude?: number;
  longitude?: number;
  updateLocation?: boolean;
  comment?: string;
  // Proof images as base64 (no data-URL prefix). The API DTO models
  // PhotoImage/SignatureImage as base64 strings.
  signatureBase64?: string;
  photoBase64?: string;
}

// Delivery confirmation (POD). Sent as multipart form fields matching
// shipmentDispatchBatchRequest. CompanyID/UserID are forced server-side from the
// token, so the client doesn't send them.
export function enqueueDispatch(input: DispatchInput): Promise<string> {
  const fields: Record<string, string | number | boolean | string[] | undefined> = {
    ShipmentID: input.shipmentId,
    Barcodes: input.barcodes,
    PodName: input.podName,
    Comment: input.comment,
    Latitude: input.latitude,
    Longitude: input.longitude,
    UpdateLocation: input.updateLocation ? 'true' : 'false',
    PhotoImage: input.photoBase64,
    SignatureImage: input.signatureBase64,
  };
  return enqueue({ kind: 'dispatch', endpoint: 'shipment/event/dispatch_batch/post', method: 'POST', isMultipart: true, payload: { fields } });
}

export function enqueueComment(shipmentId: number, comment: string): Promise<string> {
  return enqueue({ kind: 'comment', endpoint: 'shipment/comment/post', method: 'POST', payload: { shipmentId, comment } });
}

export interface ExceptionInput {
  shipmentId: number;
  reasonId: number;
  comment: string;
  // Base64 (no data-URL prefix). Only meaningful when the customer requires a
  // photo on event (Shipment.photoOnEvent).
  photoBase64?: string;
}

// Non-delivery exception. Sent as multipart form fields matching
// shipmentExceptionRequest (CompanyID/UserID forced server-side from the token).
export function enqueueException(input: ExceptionInput): Promise<string> {
  const fields: Record<string, string | number | boolean | undefined> = {
    ShipmentID: input.shipmentId,
    ReasonID: input.reasonId,
    Comment: input.comment,
    PhotoImage: input.photoBase64,
  };
  return enqueue({ kind: 'exception', endpoint: 'shipment/event/exception/post', method: 'POST', isMultipart: true, payload: { fields } });
}

export interface CodInput {
  shipmentId: number;
  codTypeId: number;
  codAmount: number;
  codCheck?: string; // check / document number
}

// Cash-On-Delivery collection. One POST per COD, matching shipmentCodRequest.
// CreatedSource marks the origin ('app') and is required by the DTO.
export function enqueueCod(input: CodInput): Promise<string> {
  const fields: Record<string, string | number | boolean | undefined> = {
    ShipmentID: input.shipmentId,
    CodTypeId: input.codTypeId,
    CodAmount: input.codAmount,
    CodCheck: input.codCheck,
    CreatedSource: 'app',
  };
  return enqueue({ kind: 'cod', endpoint: 'shipment/event/cod/post', method: 'POST', isMultipart: true, payload: { fields } });
}
