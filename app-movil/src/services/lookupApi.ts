import { api } from '@config/axios';
import type { Company } from '@services/driverApi';
import { addressLine, toClientFicha, type ClientFicha, type ContactPointLike, type StockRow } from '@utils/lookupRules';

// Consultas del personal en lectura (Lote 217): clientes, consignatarios y almacén. Son los endpoints de la web; el servidor ya exige el permiso y el módulo. Sin señal no hay resultado.

export interface ClientRow {
  publicId: string;
  code: string;
  name: string;
  status: string;
  isActive: boolean;
}

export async function searchClients(c: Company, search: string): Promise<ClientRow[]> {
  const { data } = await api.get<{ publicId: string; code: string; name: string; statusLabel: string; isActive: boolean }[]>('clients', { tenantId: c.tenantId, params: { search: search.trim() || undefined } });
  return data.slice(0, 200).map((r) => ({ publicId: r.publicId, code: r.code, name: r.name, status: r.statusLabel, isActive: r.isActive }));
}

export async function fetchClient(c: Company, publicId: string): Promise<ClientFicha> {
  const { data } = await api.get(`clients/${publicId}`, { tenantId: c.tenantId });
  return toClientFicha(data);
}

export interface ConsigneeRow {
  publicId: string;
  id: number;
  name: string;
  code: string | null;
  clientName: string | null;
  address: string;
  isActive: boolean;
}

interface LocationDto {
  id: number;
  publicId: string;
  clientName: string | null;
  code: string | null;
  name: string;
  line1: string;
  line2: string | null;
  city: string;
  state: string | null;
  postalCode: string | null;
  accessNotes: string | null;
  deliveryNotes: string | null;
  contactName?: string | null;
  isActive: boolean;
}

const toConsignee = (r: LocationDto): ConsigneeRow => ({ publicId: r.publicId, id: r.id, name: r.name, code: r.code, clientName: r.clientName, address: addressLine(r), isActive: r.isActive });

export async function searchConsignees(c: Company, search: string): Promise<ConsigneeRow[]> {
  const { data } = await api.get<LocationDto[]>('locations', { tenantId: c.tenantId, params: { search: search.trim() || undefined } });
  return data.slice(0, 200).map(toConsignee);
}

export interface ConsigneeFicha extends ConsigneeRow {
  accessNotes: string | null;
  deliveryNotes: string | null;
  contactName: string | null;
  points: ContactPointLike[];
}

export async function fetchConsignee(c: Company, row: ConsigneeRow): Promise<ConsigneeFicha> {
  const [loc, points] = await Promise.all([
    api.get<LocationDto>(`locations/${row.publicId}`, { tenantId: c.tenantId }),
    api.get<(ContactPointLike & { contactTypeLabel?: string })[]>(`contacts/LOCATION/${row.id}`, { tenantId: c.tenantId }).then((r) => r.data).catch(() => []),
  ]);
  return { ...toConsignee(loc.data), accessNotes: loc.data.accessNotes, deliveryNotes: loc.data.deliveryNotes, contactName: loc.data.contactName ?? null, points };
}

export interface ProductRow {
  publicId: string;
  sku: string;
  name: string;
  barcode: string | null;
  category: string | null;
  owner: string | null;
  brand: string | null;
  onHand: number;
  reserved: number;
  available: number;
  isActive: boolean;
}

interface ProductDto { publicId: string; sku: string; name: string; barcode: string | null; categoryName: string | null; ownerName: string | null; brand: string | null; qtyOnHand: number; qtyReserved: number; qtyAvailable: number; isActive: boolean }

// Del producto se toman solo cantidades: el costo de compra y el precio de venta que trae el servidor nunca se copian a la pantalla.
const toProduct = (p: ProductDto): ProductRow => ({
  publicId: p.publicId, sku: p.sku, name: p.name, barcode: p.barcode, category: p.categoryName, owner: p.ownerName, brand: p.brand,
  onHand: p.qtyOnHand, reserved: p.qtyReserved, available: p.qtyAvailable, isActive: p.isActive,
});

export async function searchProducts(c: Company, search: string): Promise<ProductRow[]> {
  const { data } = await api.get<{ items: ProductDto[] }>('products', { tenantId: c.tenantId, params: { search: search.trim(), take: 50, activeOnly: true } });
  return data.items.map(toProduct);
}

/** El código escaneado: código de barras o SKU exacto. Sin coincidencia el servidor contesta 404 y aquí queda null. */
export async function productByCode(c: Company, code: string): Promise<ProductRow | null> {
  try {
    const { data } = await api.get<{ product: ProductDto }>(`products/by-barcode/${encodeURIComponent(code.trim())}`, { tenantId: c.tenantId });
    return toProduct(data.product);
  } catch (e) {
    if ((e as { response?: { status?: number } }).response?.status === 404) return null;
    throw e;
  }
}

export async function fetchStock(c: Company, productPublicId: string): Promise<StockRow[]> {
  const { data } = await api.get<{ items: { warehouseCode: string; binCode: string | null; lotNumber: string | null; qtyOnHand: number; qtyReserved: number; qtyAvailable: number }[] }>(
    'inventory/balances', { tenantId: c.tenantId, params: { productPublicIds: [productPublicId], take: 200 }, paramsSerializer: { indexes: null } });
  return data.items.map((r) => ({ warehouseCode: r.warehouseCode, binCode: r.binCode, lotNumber: r.lotNumber, qtyOnHand: r.qtyOnHand, qtyReserved: r.qtyReserved, qtyAvailable: r.qtyAvailable }));
}

export interface BinHit { id: number; code: string; zone: string | null; warehouseCode: string; warehousePublicId: string }

export async function searchBins(c: Company, search: string): Promise<BinHit[]> {
  const { data } = await api.get<{ id: number; code: string; zoneCode: string | null; warehouseCode: string; warehousePublicId: string }[]>('warehouses/bins/search', { tenantId: c.tenantId, params: { search: search.trim(), take: 20 } });
  return data.map((b) => ({ id: b.id, code: b.code, zone: b.zoneCode, warehouseCode: b.warehouseCode, warehousePublicId: b.warehousePublicId }));
}

export interface BinContent { sku: string; name: string; productPublicId: string }

export async function fetchBinContent(c: Company, bin: BinHit): Promise<BinContent[]> {
  const { data } = await api.get<{ items: { products: { productPublicId: string; sku: string; name: string }[] }[] }>(`warehouses/${bin.warehousePublicId}/bin-products`, { tenantId: c.tenantId, params: { binIds: [bin.id], onlyWithStock: true, take: 5 }, paramsSerializer: { indexes: null } });
  return data.items.flatMap((i) => i.products).map((p) => ({ sku: p.sku, name: p.name, productPublicId: p.productPublicId }));
}
