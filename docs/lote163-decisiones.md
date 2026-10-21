# Lote 163 — «Envíos» en vez de «Órdenes» (transporte) y «número de envío»

Fecha: 2026-10-20. Pedido de Luis: donde diga orden en la parte de transporte (también en el portal del cliente) debe decir envío, y «número de orden» debe ser «número de envío».

## Qué se hizo
- **Web** (`es.json` y `en.json`): pantallas de transporte (envíos, sala de despacho, escaneo, reclamaciones, COD, consignatarios, clientes, integraciones, pagos de intentos, impresión, recolección y empaque, cruce de muelle, radar y avisos del Pulso). Con concordancia de género (el/los, nuevo, creado…). «N.º de orden», «Número de orden» y similares pasan a «número de envío». En inglés, «order» pasa a «shipment» en esas mismas pantallas.
- **Portal del cliente** (`app-cliente`): igual, en español e inglés.
- **Mensajes del servidor** que ve el usuario (errores 400/404/409/422, avisos, mensajes de importación, de integraciones, de escaneo, de devolución y de COD) y los nombres de campo de exportaciones y de las Vistas (fuente «Envíos», «Número de envío»).
- **Tabla de Envíos**: empaque, conduce y número ya no se parten en renglones (`ref-nowrap`).
- **Manual y FAQ**: se actualizaron los textos exactos de pantallas y mensajes que cambiaron (58 archivos).

## Cómo se probó (solo lo que cambió)
- Web: pruebas de las pantallas tocadas y las de i18n; `tsc` limpio. Portal: jest completo del portal (144).
- C#: pruebas de los mensajes cambiados, actualizadas a los textos nuevos.
- Los recorridos Playwright (`web-app/e2e`) se actualizaron pero **no se corrieron** (necesitan el API y la base).

## Lo que NO se cambió (decisiones para Luis)
1. **Estatus** (Sometida, Recogida, Asignada, Entregada, Devuelta…): son etiquetas del catálogo, concuerdan con «orden» (femenino). Con «envío» deberían ser Sometido, Recogido, Asignado, Entregado, Devuelto. Es un cambio de datos (`StatusCode`) que se haría en `logistica-db-update.sql`.
2. **Permisos** (`PermissionCatalog`: «Ver órdenes», «Crear envíos»…), **indicadores, gráficos y reportes sembrados** (`SystemAnalyticsSeeder`: «Envíos en curso»…), **documento legal** y **resúmenes del Pulso diario** (`KpiSnapshots`): son datos sembrados en la base (se buscan por nombre); cambiarlos pide una sección del SQL de actualización.
3. **Órdenes de compra, de venta, de trabajo y de recibo** no cambian (a propósito).
4. **Prosa del manual**: los textos exactos se actualizaron, pero hay párrafos del manual que siguen diciendo «orden».
5. **Código y rutas técnicas** (`orders`, `orderNumber`, `/api/v1/orders`, `$.orders[*]`) no cambian.
