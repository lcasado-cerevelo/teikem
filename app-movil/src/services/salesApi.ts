import { Platform } from 'react-native';
import { api } from '@config/axios';

// Lote 226 (P4) — el PEDIDO desde el teléfono del vendedor: los mismos endpoints del pedido guiado de la web (Lotes 221-223). Cada llamada lleva la compañía
// (`tenantId`: decide con qué token se firma) y `silentForbidden`: un 403 lo muestra la pantalla (sin el aviso general que corta el trabajo).
// Permisos (los exige el servidor): catálogo y lista sales.view; borrador, firma, comprobar y enviar sales.create; alta mínima clients.create_basic (o clients.create).
// Sin modo sin señal (decisión del plan, §1.6): el borrador se guarda en el servidor en cada paso; sin señal la pantalla avisa y no avanza.

const opts = (tenantId: number) => ({ tenantId, silentForbidden: true });

// ---------------------------------------------------------------- tipos (subconjunto de los DTO del API; ver web-app/openapi.json)

export interface SalesCatalogItem {
  productPublicId: string;
  sku: string;
  name: string;
  brand?: string | null;
  isKit?: boolean;
  kitAdjustable?: boolean;
  isPromotion?: boolean;
  promoFrom?: string | null;
  promoTo?: string | null;
  price?: number | null;
  priceSource?: string | null;
  imagePublicId?: string | null;
}

export interface SalesCatalogBrand {
  brand: string | null;
  offers: SalesCatalogItem[];
  products: SalesCatalogItem[];
}

export interface SalesCatalog {
  clientPublicId?: string | null;
  date?: string;
  brands: SalesCatalogBrand[];
}

export interface SalesOrderTerms {
  isUrgent?: boolean;
  deliveryMode?: string;
  deliveryModeSet?: boolean;
  paymentMethod?: string | null;
  paymentMethodLabel?: string | null;
  paymentTerm?: string | null;
  paymentTermLabel?: string | null;
  paymentChanged?: boolean;
  shippingCharge?: number;
  shippingTaxAmount?: number;
  shippingReason?: string | null;
  shippingReasonLabel?: string | null;
  isConsignment?: boolean;
  authorizedByName?: string | null;
  signatureFilePublicId?: string | null;
  submittedWithAuth?: string | null;
  awaitingClientApproval?: boolean;
  clientIsProspect?: boolean;
  clientRequiresSignature?: boolean;
}

export interface SalesOrderLine {
  id?: number;
  lineNo?: number;
  productPublicId?: string;
  sku?: string;
  productName?: string;
  qtyOrdered?: number;
  qtyBackordered?: number;
  unitPrice?: number;
  lineSubtotal?: number;
  isKit?: boolean;
}

export interface SalesOrder {
  publicId: string;
  number: string;
  status: string;
  statusLabel?: string;
  clientPublicId?: string;
  clientName?: string;
  consigneePublicId?: string | null;
  consigneeName?: string | null;
  warehousePublicId?: string;
  requiredDate?: string | null;
  notes?: string | null;
  subtotal?: number;
  taxAmount?: number;
  total?: number;
  hasBackorder?: boolean;
  createdAtUtc?: string;
  lines?: SalesOrderLine[];
  terms?: SalesOrderTerms | null;
}

export interface SalesOrderRow {
  publicId: string;
  number: string;
  status: string;
  statusLabel?: string;
  clientName?: string;
  requestedDate?: string;
  total?: number;
  lineCount?: number;
  hasBackorder?: boolean;
  requestedByName?: string | null;
  createdAtUtc?: string;
  isUrgent?: boolean;
  awaitingClientApproval?: boolean;
  isConsignment?: boolean;
  paymentChanged?: boolean;
  deliveryMode?: string | null;
}

export interface SalesAvailability {
  lines: { lineNo: number; sku: string; productName: string; ordered: number; available: number; shortage: number }[];
  hasShortage: boolean;
  credit?: { limit?: number | null; exposure?: number; newAmount?: number; available?: number | null; exceeded?: boolean; message?: string | null } | null;
}

export interface ClientHit {
  publicId: string;
  code: string;
  name: string;
  status: string;
  statusLabel?: string;
}

export interface ClientAddress {
  publicId?: string;
  line1?: string | null;
  line2?: string | null;
  city?: string | null;
  postalCode?: string | null;
  isActive?: boolean;
}

/** Lo que el pedido usa de la ficha del cliente: estatus (prospecto), forma de pago, dirección física y si exige la firma de quien autoriza. */
export interface ClientForOrder {
  publicId: string;
  code: string;
  name: string;
  status: string;
  paymentTerm?: string | null;
  physicalAddress?: ClientAddress | null;
  requireOrderSignature?: boolean;
}

export interface LookupOption {
  code: string;
  label: string;
}

export interface ConsigneeOption {
  publicId: string;
  name: string;
  address: string;
}

// ---------------------------------------------------------------- consultas

export async function fetchCatalog(tenantId: number, clientPublicId: string): Promise<SalesCatalog> {
  const { data } = await api.get<SalesCatalog>('sales-orders/catalog', { ...opts(tenantId), params: { clientPublicId } });
  return { ...data, brands: (data.brands ?? []).map((b) => ({ brand: b.brand ?? null, offers: b.offers ?? [], products: b.products ?? [] })) };
}

export async function searchClientsForOrder(tenantId: number, search: string): Promise<ClientHit[]> {
  const { data } = await api.get<ClientHit[]>('clients', { ...opts(tenantId), params: { search: search.trim(), includeInactive: false } });
  return (data ?? []).slice(0, 30);
}

export async function fetchClientForOrder(tenantId: number, publicId: string): Promise<ClientForOrder> {
  const { data } = await api.get<ClientForOrder>(`clients/${publicId}`, opts(tenantId));
  return {
    publicId: data.publicId, code: data.code, name: data.name, status: data.status, paymentTerm: data.paymentTerm ?? null,
    physicalAddress: data.physicalAddress ?? null, requireOrderSignature: !!data.requireOrderSignature,
  };
}

/** Un catálogo (LookupCode) con sus etiquetas en el idioma de la sesión: SalesPaymentMethod, PaymentTerm. */
export async function fetchLookups(tenantId: number, entity: string): Promise<LookupOption[]> {
  const { data } = await api.get<{ code?: string; label?: string; sortOrder?: number; isEnabled?: boolean }[]>(`catalogs/${entity}`, { ...opts(tenantId), params: { includeDisabled: false } });
  return (data ?? [])
    .filter((o) => o.code && o.isEnabled !== false)
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    .map((o) => ({ code: o.code ?? '', label: o.label || o.code || '' }));
}

/** Almacenes activos (para saber si hay que escoger uno). Sin permiso de verlos responde vacío: el servidor usa el único que haya. */
export async function fetchWarehouses(tenantId: number): Promise<LookupOption[]> {
  try {
    const { data } = await api.get<{ publicId: string; code: string; name: string }[]>('warehouses', { ...opts(tenantId), params: { includeInactive: false } });
    return (data ?? []).map((w) => ({ code: w.publicId, label: `${w.code} · ${w.name}` }));
  } catch {
    return [];
  }
}

/** Consignatarios de entrega del cliente (propios y compartidos); solo con locations.read. */
export async function fetchClientConsignees(tenantId: number, clientPublicId: string): Promise<ConsigneeOption[]> {
  const { data } = await api.get<{ publicId: string; name: string; line1?: string | null; city?: string | null; postalCode?: string | null }[]>('locations', {
    ...opts(tenantId),
    params: { locationType: 'DELIVERY', includeInactive: false, clientId: clientPublicId, includeShared: true },
  });
  return (data ?? []).map((l) => ({ publicId: l.publicId, name: l.name, address: [l.line1, [l.city, l.postalCode].filter(Boolean).join(' ')].filter(Boolean).join(', ') }));
}

/** ¿La cuenta tiene MFA? (para pedir el código junto a la contraseña al enviar). */
export async function fetchMfaEnabled(tenantId: number): Promise<boolean> {
  try {
    const { data } = await api.get<{ mfaEnabled?: boolean }>('me', opts(tenantId));
    return !!data.mfaEnabled;
  } catch {
    return false;
  }
}

/** «Mis pedidos»: las órdenes de la compañía por estatus y buscador (las más nuevas primero); la pantalla se queda con las que capturó la persona. */
export async function listSalesOrders(tenantId: number, status: string, q: string): Promise<SalesOrderRow[]> {
  const { data } = await api.get<{ items: SalesOrderRow[]; total: number }>('sales-orders', {
    ...opts(tenantId),
    params: { status: status || undefined, q: q.trim() || undefined, skip: 0, take: 200 },
  });
  return data.items ?? [];
}

export async function fetchSalesOrder(tenantId: number, publicId: string): Promise<SalesOrder> {
  const { data } = await api.get<SalesOrder>(`sales-orders/${publicId}`, opts(tenantId));
  return data;
}

// ---------------------------------------------------------------- el borrador por pasos, la firma y enviar

export async function createDraft(tenantId: number, body: object): Promise<SalesOrder> {
  const { data } = await api.post<SalesOrder>('sales-orders', body, opts(tenantId));
  return data;
}

export async function updateDraft(tenantId: number, publicId: string, body: object): Promise<SalesOrder> {
  const { data } = await api.patch<SalesOrder>(`sales-orders/${publicId}`, body, opts(tenantId));
  return data;
}

/** Un archivo para un multipart: en el teléfono basta { uri, name, type } (también con un data: URI); en la web hace falta un Blob. */
async function filePart(uri: string, name: string, type: string): Promise<Blob | { uri: string; name: string; type: string }> {
  if (Platform.OS === 'web') return (await fetch(uri)).blob();
  return { uri, name, type };
}

async function postMultipart<T>(tenantId: number, url: string, form: FormData): Promise<T> {
  const { data } = await api.post<T>(url, form, { ...opts(tenantId), headers: { 'Content-Type': 'multipart/form-data' }, timeout: 60000 });
  return data;
}

/** La firma dibujada (PNG del lienzo, en base64) y el nombre de quien autoriza. */
export async function uploadSignature(tenantId: number, publicId: string, pngBase64: string, authorizedByName: string): Promise<SalesOrder> {
  const form = new FormData();
  form.append('file', (await filePart(`data:image/png;base64,${pngBase64}`, 'firma.png', 'image/png')) as unknown as Blob, 'firma.png');
  form.append('authorizedByName', authorizedByName);
  return postMultipart<SalesOrder>(tenantId, `sales-orders/${publicId}/signature`, form);
}

export async function removeSignature(tenantId: number, publicId: string): Promise<SalesOrder> {
  const { data } = await api.post<SalesOrder>(`sales-orders/${publicId}/signature/remove`, undefined, opts(tenantId));
  return data;
}

export async function checkAvailability(tenantId: number, publicId: string): Promise<SalesAvailability> {
  const { data } = await api.get<SalesAvailability>(`sales-orders/${publicId}/availability`, opts(tenantId));
  return data;
}

/** «Enviar pedido»: reautenticación (BIOMETRIC o PASSWORD) y solicitar en la misma llamada. */
export async function submitOrder(tenantId: number, publicId: string, body: object): Promise<SalesOrder> {
  const { data } = await api.post<SalesOrder>(`sales-orders/${publicId}/submit`, body, opts(tenantId));
  return data;
}

// ---------------------------------------------------------------- cliente nuevo

export async function createBasicClient(tenantId: number, body: object): Promise<ClientForOrder> {
  const { data } = await api.post<ClientForOrder>('clients/basic', body, opts(tenantId));
  return {
    publicId: data.publicId, code: data.code, name: data.name, status: data.status, paymentTerm: data.paymentTerm ?? null,
    physicalAddress: data.physicalAddress ?? null, requireOrderSignature: !!data.requireOrderSignature,
  };
}

/** Foto de un documento del cliente (registro de comerciante, certificado de revendedor), tomada con la cámara o escogida de la galería. */
export async function uploadClientDocument(tenantId: number, clientPublicId: string, documentType: string, photoUri: string): Promise<void> {
  const form = new FormData();
  const name = `${documentType.toLowerCase()}.jpg`;
  form.append('file', (await filePart(photoUri, name, 'image/jpeg')) as unknown as Blob, name);
  form.append('documentType', documentType);
  await postMultipart(tenantId, `clients/${clientPublicId}/documents`, form);
}

/** Foto de una oferta (StoredFile): se pide con la firma de la compañía. */
export function fileSource(tenantId: number, filePublicId: string, token: string | null): { uri: string; headers: Record<string, string> } {
  const base = (api.defaults.baseURL ?? '').endsWith('/') ? api.defaults.baseURL ?? '' : `${api.defaults.baseURL ?? ''}/`;
  return { uri: `${base}files/${filePublicId}`, headers: { Authorization: `Bearer ${token ?? ''}` } };
}
