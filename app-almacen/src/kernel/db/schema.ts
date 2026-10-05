// Lote 8A-app — esquema de la base local (expo-sqlite). Ver docs/mobile/app-almacen-plan.md §1 "Base local".
// Migraciones por PRAGMA user_version: cada versión agrega su bloque de SQL; nunca se reescribe uno ya publicado.
export const SCHEMA_VERSION = 6

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
  // v2 (segunda entrega): Despacho y Conteo empiezan con una llamada en línea (reclaman un recurso compartido: el
  // producto sale de una posición real, el conteo bloquea la posición para otros) y de ahí en adelante trabajan sin
  // señal; sus documentos locales sobreviven a cerrar la app igual que local_receipt. Consultar guarda una copia con
  // fecha de lo último que preguntó en línea, para poder responder "de hace N min" sin red.
  `
  -- Despacho (recolectar y empacar): un despacho local a la vez, igual que el recibo.
  CREATE TABLE IF NOT EXISTS local_pick (
    id INTEGER PRIMARY KEY NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    client_public_id TEXT,
    client_name TEXT,
    created_at_utc TEXT NOT NULL
  );

  CREATE TABLE IF NOT EXISTS local_pick_line (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    pick_id INTEGER NOT NULL,
    product_public_id TEXT NOT NULL,
    sku TEXT,
    product_name TEXT,
    quantity REAL NOT NULL,
    from_bin_code TEXT NOT NULL,
    from_bin_id INTEGER,
    serial_numbers TEXT,
    FOREIGN KEY (pick_id) REFERENCES local_pick(id) ON DELETE CASCADE
  );
  CREATE INDEX IF NOT EXISTS ix_local_pick_line_pick ON local_pick_line(pick_id);

  -- Conteo: el conteo en sí (count_id) ya existe en el servidor desde que se escanea la posición (POST /cycle-counts,
  -- en línea, reclama la posición); las líneas capturadas se guardan aquí y se mandan en lote al terminar.
  CREATE TABLE IF NOT EXISTS local_count (
    id INTEGER PRIMARY KEY NOT NULL,
    count_id INTEGER NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    bin_id INTEGER NOT NULL,
    bin_code TEXT,
    is_blind INTEGER NOT NULL DEFAULT 1,
    created_at_utc TEXT NOT NULL
  );

  CREATE TABLE IF NOT EXISTS local_count_line (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    local_count_id INTEGER NOT NULL,
    line_id INTEGER,
    product_public_id TEXT NOT NULL,
    sku TEXT,
    product_name TEXT,
    system_qty REAL,
    counted_qty REAL NOT NULL,
    serial_numbers TEXT,
    is_extra INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (local_count_id) REFERENCES local_count(id) ON DELETE CASCADE
  );
  CREATE INDEX IF NOT EXISTS ix_local_count_line_count ON local_count_line(local_count_id);

  -- Consultar: última respuesta en línea de saldos, por si se repite la pregunta sin red ("datos de hace N min").
  CREATE TABLE IF NOT EXISTS balance_cache (
    cache_key TEXT PRIMARY KEY NOT NULL,
    payload_json TEXT NOT NULL,
    fetched_at_utc TEXT NOT NULL
  );
  `,
  // v3 (Lote 16, recibo directo a posición): el recibo guarda el modo con que se abrió (copia del modo del almacén del
  // aparato; NULL = recibo abierto antes de actualizar la app, se manda sin modo y el servidor lo trata "con acomodo") y
  // cada línea su posición destino escaneada. Las posiciones del almacén se guardan en la tabla `bin` de la v1 (existía
  // desde el principio para el catálogo sincronizado, pero nadie la llenaba): la llena kernel/sync/download.ts desde
  // GET /sync/bins y Recibir valida contra ella sin señal. Índice por almacén y código sin distinguir mayúsculas.
  `
  ALTER TABLE local_receipt ADD COLUMN receiving_mode TEXT;
  ALTER TABLE local_receipt_line ADD COLUMN target_bin_code TEXT;
  CREATE INDEX IF NOT EXISTS ix_bin_warehouse_code ON bin(warehouse_public_id, code COLLATE NOCASE);
  `,
  // v4 (Lote A4, "Contar por producto", docs/conteo-por-producto-diseno.md): un conteo local puede ser por POSICIÓN (como
  // antes) o por PRODUCTO (varias posiciones). La posición del conteo pasa a ser opcional (bin_id deja de ser NOT NULL) y
  // cada línea guarda la suya (bin_id, bin_code), su lote (lot_id, lot_number y, para un lote nuevo de "Otra posición",
  // lot_expiry_date) y si la posición es provisional; counted_qty admite NULL = espacio en blanco (se manda como 0).
  // SQLite no quita un NOT NULL con ALTER: se reconstruyen las dos tablas copiando todo (las líneas de un conteo por posición
  // en curso heredan la posición de su conteo). Con las llaves foráneas apagadas (si no, borrar la tabla vieja borraría en
  // cascada las líneas) y en una transacción, que también fija user_version: o queda migrada entera o no se toca.
  `
  PRAGMA foreign_keys = OFF;
  BEGIN;
  CREATE TABLE local_count_v4 (
    id INTEGER PRIMARY KEY NOT NULL,
    count_id INTEGER NOT NULL,
    warehouse_public_id TEXT NOT NULL,
    mode TEXT NOT NULL DEFAULT 'BIN',
    bin_id INTEGER,
    bin_code TEXT,
    product_public_id TEXT,
    sku TEXT,
    product_name TEXT,
    tracking_type_code TEXT,
    is_blind INTEGER NOT NULL DEFAULT 1,
    created_at_utc TEXT NOT NULL
  );
  INSERT INTO local_count_v4 (id, count_id, warehouse_public_id, mode, bin_id, bin_code, is_blind, created_at_utc)
    SELECT id, count_id, warehouse_public_id, 'BIN', bin_id, bin_code, is_blind, created_at_utc FROM local_count;

  CREATE TABLE local_count_line_v4 (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    local_count_id INTEGER NOT NULL,
    line_id INTEGER,
    product_public_id TEXT NOT NULL,
    sku TEXT,
    product_name TEXT,
    system_qty REAL,
    counted_qty REAL,
    serial_numbers TEXT,
    is_extra INTEGER NOT NULL DEFAULT 0,
    bin_id INTEGER,
    bin_code TEXT,
    lot_id INTEGER,
    lot_number TEXT,
    lot_expiry_date TEXT,
    is_provisional_bin INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (local_count_id) REFERENCES local_count(id) ON DELETE CASCADE
  );
  INSERT INTO local_count_line_v4 (id, local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty,
                                   serial_numbers, is_extra, bin_id, bin_code)
    SELECT l.id, l.local_count_id, l.line_id, l.product_public_id, l.sku, l.product_name, l.system_qty, l.counted_qty,
           l.serial_numbers, l.is_extra, c.bin_id, c.bin_code
    FROM local_count_line l LEFT JOIN local_count c ON c.id = l.local_count_id;

  DROP TABLE local_count_line;
  DROP TABLE local_count;
  ALTER TABLE local_count_v4 RENAME TO local_count;
  ALTER TABLE local_count_line_v4 RENAME TO local_count_line;
  CREATE INDEX IF NOT EXISTS ix_local_count_line_count ON local_count_line(local_count_id);
  PRAGMA user_version = 4;
  COMMIT;
  PRAGMA foreign_keys = ON;
  `,
  // v5 (Lote A4, adenda 5d): la tabla `bin` guarda si la posición es provisional ("pendiente de revisión", la crea el operario
  // desde un conteo): GET /sync/bins ya lo trae (isProvisional) y la descarga lo copia. Solo se agrega la columna (los datos se
  // conservan, por defecto 0). Como las posiciones ya descargadas no la traen, se borra la marca de agua de las posiciones para
  // que la próxima sincronización las baje completas una vez y quede puesta la marca de las que ya eran provisionales.
  `
  ALTER TABLE bin ADD COLUMN is_provisional INTEGER NOT NULL DEFAULT 0;
  DELETE FROM sync_watermark WHERE resource LIKE 'bins:%';
  `,
  // v6 (2026-10-05, orden de salida): copia del aparato de GET /inventory/exit-options del almacén por defecto: por producto, las
  // existencias DISPONIBLES en el orden de salida del servidor (rank 1 = sale primero, FEFO), para sugerir/exigir de dónde sale lo que se
  // despacha también sin señal. Es una foto (se reemplaza completa o por producto), no una bajada por diferencia.
  `
  CREATE TABLE IF NOT EXISTS stock_exit (
    warehouse_public_id TEXT NOT NULL,
    product_public_id TEXT NOT NULL,
    rank INTEGER NOT NULL,
    bin_id INTEGER NOT NULL,
    bin_code TEXT NOT NULL,
    zone_code TEXT,
    zone_type_code TEXT,
    lot_id INTEGER,
    lot_number TEXT,
    expiry_date TEXT,
    available REAL NOT NULL,
    PRIMARY KEY (warehouse_public_id, product_public_id, rank)
  );
  `,
]
