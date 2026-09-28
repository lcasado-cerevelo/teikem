// Lote 8A-app — esquema de la base local (expo-sqlite). Ver docs/mobile/app-almacen-plan.md §1 "Base local".
// Migraciones por PRAGMA user_version: cada versión agrega su bloque de SQL; nunca se reescribe uno ya publicado.
export const SCHEMA_VERSION = 1

export const MIGRATIONS: readonly string[] = [
  // v1: kv, catálogos sincronizados, documentos abiertos, cola de salida y marcas de agua.
  `
  CREATE TABLE IF NOT EXISTS kv (
    key TEXT PRIMARY KEY NOT NULL,
    value TEXT
  );

  CREATE TABLE IF NOT EXISTS product (
    id INTEGER PRIMARY KEY NOT NULL,
    public_id TEXT NOT NULL UNIQUE,
    sku TEXT NOT NULL,
    name TEXT NOT NULL,
    barcode TEXT,
    tracking_type_code TEXT,
    base_uom_code TEXT,
    category_id INTEGER,
    owner_client_public_id TEXT,
    owner_name TEXT,
    preferred_bin_id INTEGER,
    is_active INTEGER NOT NULL DEFAULT 1
  );
  CREATE INDEX IF NOT EXISTS ix_product_barcode ON product(barcode);
  CREATE INDEX IF NOT EXISTS ix_product_sku ON product(sku);

  CREATE TABLE IF NOT EXISTS bin (
    id INTEGER PRIMARY KEY NOT NULL,
    code TEXT NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    zone_id INTEGER,
    zone_code TEXT,
    zone_name TEXT,
    zone_type_code TEXT,
    aisle TEXT,
    rack TEXT,
    level TEXT,
    position TEXT,
    is_active INTEGER NOT NULL DEFAULT 1
  );
  CREATE INDEX IF NOT EXISTS ix_bin_code ON bin(code);
  CREATE INDEX IF NOT EXISTS ix_bin_warehouse ON bin(warehouse_public_id);

  CREATE TABLE IF NOT EXISTS product_category (
    id INTEGER PRIMARY KEY NOT NULL,
    name TEXT NOT NULL,
    parent_id INTEGER,
    is_active INTEGER NOT NULL DEFAULT 1
  );

  CREATE TABLE IF NOT EXISTS purchase_order (
    id INTEGER PRIMARY KEY NOT NULL,
    public_id TEXT NOT NULL UNIQUE,
    number TEXT NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    supplier_name TEXT,
    status_code TEXT,
    expected_date TEXT,
    is_active INTEGER NOT NULL DEFAULT 1
  );
  CREATE INDEX IF NOT EXISTS ix_purchase_order_number ON purchase_order(number);

  CREATE TABLE IF NOT EXISTS purchase_order_line (
    id INTEGER PRIMARY KEY NOT NULL,
    purchase_order_id INTEGER NOT NULL,
    product_public_id TEXT NOT NULL,
    sku TEXT,
    product_name TEXT,
    qty_ordered REAL NOT NULL DEFAULT 0,
    qty_received REAL NOT NULL DEFAULT 0,
    qty_pending REAL NOT NULL DEFAULT 0
  );
  CREATE INDEX IF NOT EXISTS ix_po_line_order ON purchase_order_line(purchase_order_id);

  CREATE TABLE IF NOT EXISTS asn (
    id INTEGER PRIMARY KEY NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    client_public_id TEXT,
    client_name TEXT,
    purchase_order_public_id TEXT,
    purchase_order_number TEXT,
    reference TEXT,
    expected_date TEXT,
    status_code TEXT,
    is_active INTEGER NOT NULL DEFAULT 1
  );
  CREATE INDEX IF NOT EXISTS ix_asn_po_number ON asn(purchase_order_number);
  CREATE INDEX IF NOT EXISTS ix_asn_reference ON asn(reference);

  CREATE TABLE IF NOT EXISTS asn_line (
    id INTEGER PRIMARY KEY NOT NULL,
    asn_id INTEGER NOT NULL,
    product_public_id TEXT NOT NULL,
    sku TEXT,
    product_name TEXT,
    expected_qty REAL NOT NULL DEFAULT 0,
    lot_number TEXT,
    purchase_order_line_id INTEGER
  );
  CREATE INDEX IF NOT EXISTS ix_asn_line_asn ON asn_line(asn_id);

  CREATE TABLE IF NOT EXISTS warehouse_task (
    id INTEGER PRIMARY KEY NOT NULL,
    type_code TEXT,
    status_code TEXT,
    warehouse_public_id TEXT NOT NULL,
    product_public_id TEXT,
    sku TEXT,
    product_name TEXT,
    lot_id INTEGER,
    lot_number TEXT,
    quantity REAL,
    from_bin_id INTEGER,
    from_bin_code TEXT,
    to_bin_id INTEGER,
    to_bin_code TEXT,
    ref_entity_code TEXT,
    is_active INTEGER NOT NULL DEFAULT 1
  );
  CREATE INDEX IF NOT EXISTS ix_task_warehouse ON warehouse_task(warehouse_public_id);
  CREATE INDEX IF NOT EXISTS ix_task_type ON warehouse_task(type_code);

  -- Recibo local en curso (uno por vez por aparato; se borra al confirmar o cancelar).
  CREATE TABLE IF NOT EXISTS local_receipt (
    id INTEGER PRIMARY KEY NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    purchase_order_public_id TEXT,
    asn_id INTEGER,
    doc_label TEXT,
    created_at_utc TEXT NOT NULL
  );

  CREATE TABLE IF NOT EXISTS local_receipt_line (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    receipt_id INTEGER NOT NULL,
    product_public_id TEXT NOT NULL,
    sku TEXT,
    product_name TEXT,
    tracking_type_code TEXT,
    received_qty REAL NOT NULL,
    lot_number TEXT,
    expiry_date TEXT,
    serial_numbers TEXT,
    FOREIGN KEY (receipt_id) REFERENCES local_receipt(id) ON DELETE CASCADE
  );
  CREATE INDEX IF NOT EXISTS ix_local_receipt_line_receipt ON local_receipt_line(receipt_id);

  -- Cola de operaciones pendientes de enviar (motor de sincronización, kernel/sync/engine.ts).
  CREATE TABLE IF NOT EXISTS outbox (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    idempotency_key TEXT NOT NULL UNIQUE,
    kind TEXT NOT NULL,
    method TEXT NOT NULL,
    path TEXT NOT NULL,
    body TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    attempts INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    result_json TEXT
  );
  CREATE INDEX IF NOT EXISTS ix_outbox_status ON outbox(status, created_at_utc);

  -- Marca de agua de la bajada por diferencia (kernel/sync/engine.ts), una fila por recurso.
  CREATE TABLE IF NOT EXISTS sync_watermark (
    resource TEXT PRIMARY KEY NOT NULL,
    since_utc TEXT,
    last_run_utc TEXT
  );
  `,
]
