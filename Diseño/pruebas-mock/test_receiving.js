const {load,ok,done}=require('./harness');
const {w,errs}=load();const E=c=>w.eval(c);
const total=sku=>E(`BINS.reduce((s,b)=>s+(b.stock.filter(x=>x.sku==='${sku}').reduce((a,x)=>a+x.qty,0)),0)`);
// estado inicial
ok(E("WAREHOUSES[0].receivingMode")==='PUTAWAY','almacén arranca con acomodo');
ok(E("WAREHOUSES[0].defaultRecvBin")==='S-01','posición de recepción por defecto S-01');
ok(E("ZONES.some(z=>z.type==='Recepción')")===true&&E("BINS.some(b=>b.code==='S-01')"),'zona y posiciones de recepción');
ok(E("RECEIPTS.every(r=>r.mode==='PUTAWAY'&&r.wh==='WH-01')"),'cada recibo guarda su modo');
// ---- modo con acomodo: REC-3301 (abierto)
E("selectReceipt('REC-3301')");
const sku='GLU-100';const antes=total(sku);const p0=E(`PRODUCTS.find(p=>p.sku==='${sku}').total`);
E("go('recibo')");ok(/Posición destino/.test(w.document.getElementById('wrap').innerHTML),'columna Posición destino');
E("confirmReceipt()");
ok(E("RECEIPTS.find(r=>r.id==='REC-3301').status")==='putaway','con acomodo: queda Acomodo pendiente');
ok(total(sku)===antes+40,'la mercancía entra a la posición de recepción');
ok(E("BINS.find(b=>b.code==='S-01').stock.some(x=>x.sku==='GLU-100'&&x.qty===40)"),'queda en S-01');
ok(E(`PRODUCTS.find(p=>p.sku==='${sku}').total`)===p0+40,'el total del producto sube al confirmar');
// no se puede acomodar sin destino
E("confirmPutaway()");ok(E("RECEIPTS.find(r=>r.id==='REC-3301').status")==='putaway','sin destino no se acomoda');
E("RECEIPTS.find(r=>r.id==='REC-3301').lines.forEach((l,i)=>setLineTarget(i,'B-01'))");
E("confirmPutaway()");
ok(E("RECEIPTS.find(r=>r.id==='REC-3301').status")==='done','acomodo confirmado → Completado');
ok(E("BINS.find(b=>b.code==='S-01').stock.length")===0,'S-01 queda vacía tras el acomodo');
ok(E("BINS.find(b=>b.code==='B-01').stock.some(x=>x.sku==='GLU-100')"),'la mercancía llegó a B-01');
ok(total(sku)===antes+40,'total en posiciones no cambia al acomodar');
// ---- contabilidad: lo confirmado cuenta aunque falte el acomodo
// ---- modo directo
E("setWhRecvMode('WH-01','DIRECT')");
ok(E("WAREHOUSES[0].receivingMode")==='DIRECT','almacén pasa a directo');
ok(E("RECEIPTS.filter(r=>r.id==='REC-3298'||r.id==='REC-3310').every(r=>r.mode==='PUTAWAY')"),'recibos abiertos conservan su modo');
E("selectReceipt('REC-3310');setReceiptMode('DIRECT')");
ok(E("RECEIPTS.find(r=>r.id==='REC-3310').mode")==='DIRECT','un recibo abierto cambia solo su modo');
const f0=total('FLT-O2');
E("confirmReceipt()");ok(E("RECEIPTS.find(r=>r.id==='REC-3310').status")==='expected','directo sin posición: no confirma');
E("RECEIPTS.find(r=>r.id==='REC-3310').lines.forEach((l,i)=>setLineTarget(i,'B-06'))");
E("confirmReceipt()");
ok(E("RECEIPTS.find(r=>r.id==='REC-3310').status")==='done','directo: Completado de una vez');
ok(total('FLT-O2')===f0+40,'directo: entra a la posición escogida');
ok(E("BINS.find(b=>b.code==='S-01').stock.length")===0,'directo: nada pasa por recepción');
// un recibo cerrado no cambia de modo
E("selectReceipt('REC-3295');setReceiptMode('DIRECT')");ok(E("RECEIPTS.find(r=>r.id==='REC-3295').mode")==='PUTAWAY','recibo cerrado no cambia de modo');
// pantallas con ambos modos y estados, en ambos idiomas
for(const lang of ['es','en']){E(`LANG='${lang}';applyStatic()`);
 for(const id of ['REC-3301','REC-3298','REC-3310','REC-3295']){E(`selectReceipt('${id}')`);const h=w.document.getElementById('wrap').innerHTML;ok(!/undefined|NaN/.test(h),`${lang} ${id} sin undefined`)}
 E("go('almacenes')");ok(/Recepción|Receiving/.test(w.document.getElementById('wrap').innerHTML),`${lang} almacén muestra sección de recepción`);
 E("go('ubicaciones')");E("go('inventario')");}
E("setWhRecvMode('WH-01','PUTAWAY')");E("go('almacenes')");
ok(w.document.getElementById('wrap').innerHTML.includes('S-01'),'modo con acomodo muestra la posición por defecto');
ok(errs.length===0,'sin errores: '+errs.join('|'));
done();
