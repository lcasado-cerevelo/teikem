// Domain models — mirror the entities returned by the colavora API.
// The API follows an "ID list -> detail by ID" pattern: list endpoints return
// arrays of IDs, then each entity is fetched individually.

export interface Driver {
  active: boolean;
  companyID: string;
  driverID: number;
  driverName: string;
  userID: number;
}

export interface Company {
  companyID: string;
  companyName: string;
  logo: string | null;
  slogan: string | null;
  tel1: string | null;
  ext: string | null;
  tel2: string | null;
  address1: string | null;
  address2: string | null;
  city: string | null;
  state: string | null;
  zip: string | null;
  contact: string | null;
  position: string | null;
  contactEmail: string | null;
  companyEmail: string | null;
}

export interface Manifest {
  companyID: string;
  shipmentID: number;
  manifestID: number;
  manifest: string;
  driverID: number;
  createdDate: string;
}

export interface Shipment {
  // identification
  shipmentID: number;
  waybill: string;
  barcode: string | null;
  referenceNo: string | null;
  // "Número de orden" para el buscador. Opcional: requiere el API desplegado
  // con PONumber en la respuesta (ver ShipmentDao).
  poNumber?: string | null;
  // service
  serviceType: number;
  serviceTypeName: string | null;
  packageType: number;
  // dates (ISO strings)
  readyDate: string | null;
  dueDate: string | null;
  createdDate: string | null;
  lastTransferDate: string | null;
  // COD
  codType: string | null;
  codAmount: number;
  // sender
  sender: string | null;
  senderName: string | null;
  senderAddressLine1: string | null;
  senderAddressLine2: string | null;
  senderZip: string | null;
  senderPhoneNumber: string | null;
  senderContactPerson: string | null;
  // consignee
  consigneeNum: string | null;
  consigneeName: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  zip: string | null;
  phoneNumber: string | null;
  contactPerson: string | null;
  // operational
  orderNotes: string | null;
  status: string | null;
  qty: number;
  items: number | null;
  templateID: number | null;
  manifestDL: string | null;
  manifestPk: string | null;
  assignPK: number | null;
  assignDL: number | null;
  division: string | null;
  lastEventComment: string | null;
  reason: string | null;
  // Per-customer requirement flags (Customers table bit columns, returned by the
  // shipment detail SP). Drive whether photo/signature are mandatory in the
  // Acciones tab: photoOnEvent -> exception photo; photoOnDelivery -> delivery
  // photo; signatureOnDelivery -> delivery signature.
  photoOnEvent: boolean;
  photoOnDelivery: boolean;
  signatureOnDelivery: boolean;
  // optional geo (resolved client-side or returned by detail)
  city?: string | null;
  state?: string | null;
}

// A non-delivery reason (catalog, per company/customer). From common/reason.
export interface Reason {
  reasonID: number;
  companyID: string | null;
  reasonCode: string | null;
  reasonDesc: string;
  completeOrder: boolean;
}

// A Cash-On-Delivery collection type (catalog). From common/cod.
export interface CodType {
  companyID: string | null;
  codTypeID: number;
  codType: string;
}

export interface Piece {
  companyID: string;
  shipmentID: number;
  pieceID: number;
  barcode: string;
  packageType: number;
  packageTypeName: string | null;
  comments: string | null;
  pwBack: boolean | null;
  pod: boolean | null;
}

export interface Status {
  companyID: string;
  statusID: number;
  status: string;
  completed: boolean;
}

// Geo coordinate used by the map / delivery documentation flow.
export interface LatLng {
  lat: number;
  lng: number;
}
