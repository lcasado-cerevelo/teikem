// Platform-neutral storage contract. Native uses SQLite (db.ts); web uses an
// in-memory queue + localStorage (db.web.ts). Metro picks the right file.

export type OutboxStatus = 'pending' | 'done' | 'failed';
export type OutboxMethod = 'POST' | 'PUT' | 'DELETE';

export interface OutboxRecord {
  id: string;
  kind: string;
  endpoint: string;
  method: OutboxMethod;
  payload: string; // JSON string
  isMultipart: boolean;
  status: OutboxStatus;
  retryCount: number;
  lastError: string | null;
  createdAt: number;
  updatedAt: number;
}

// Lote 216 — descarga del escaneo sin señal: envíos no completados con sus piezas (hasta 5000 por compañía). Va aparte de la cola y de la caché de consultas.
export interface ScanPieceRecord { number: string; actions: string[]; missing: string | null }
export interface ScanOrderRecord {
  publicId: string;
  orderNumber: string;
  waybill: string | null;
  statusCode: string;
  tripCode: string | null;
  tripPublicId: string | null;
  pickUp: boolean;
  pieces: ScanPieceRecord[];
}
/** Una pieza con su envío (varios si el número se repite entre envíos). */
export interface ScanPieceMatch { order: ScanOrderRecord }

export interface OfflineStorage {
  init(): Promise<void>;

  // Outbox (durable write queue)
  insertOutbox(rec: OutboxRecord): Promise<void>;
  getById(id: string): Promise<OutboxRecord | null>;
  listPending(limit: number): Promise<OutboxRecord[]>;
  countByStatus(status: OutboxStatus): Promise<number>;
  updateOutbox(
    id: string,
    fields: Partial<Pick<OutboxRecord, 'status' | 'retryCount' | 'lastError' | 'updatedAt'>>,
  ): Promise<void>;
  clearOutbox(): Promise<void>;

  // Descarga del escaneo sin señal. Se baja a una «generación» nueva y solo al confirmarla reemplaza a la anterior (una descarga a medias nunca deja el teléfono sin datos).
  scanNewGeneration(tenantId: number): Promise<number>;
  scanInsertOrders(tenantId: number, gen: number, orders: ScanOrderRecord[]): Promise<void>;
  scanCommit(tenantId: number, gen: number): Promise<void>;
  scanAbort(tenantId: number, gen: number): Promise<void>;
  scanFindPiece(tenantId: number, number: string): Promise<ScanPieceMatch[]>;
  /** Anota en lo bajado que la pieza se escaneó con la acción y, si cambió, el estatus nuevo del envío. */
  scanRecord(tenantId: number, orderPublicId: string, pieceNumber: string, action: string, newStatus: string | null): Promise<void>;
  scanOrderCount(tenantId: number): Promise<number>;
  scanClear(tenantId?: number): Promise<void>;

  // Key-value (react-query persisted cache)
  kvGet(key: string): Promise<string | null>;
  kvSet(key: string, value: string): Promise<void>;
  kvDel(key: string): Promise<void>;
}
