const {load,ok,done}=require('./harness');
const {w,errs}=load();
const E=(c)=>w.eval(c);
ok(errs.length===0,'sin errores al cargar: '+errs.join('|'));
// formatos base (Puerto Rico)
ok(E("fmtMoney(1234.5)")==='$1,234.50','money PR: '+E("fmtMoney(1234.5)"));
ok(E("fmtMoney(-12)")==='−$12.00','money neg: '+E("fmtMoney(-12)"));
ok(E("money(61023.125,3)")==='61,023.125','num 3 dec');
ok(E("fmtDate('2026-10-02')")==='10/02/2026','fecha MDY');
ok(E("fmtDayMonth('2026-10-02')")==='10/02','dia-mes MDY');
ok(E("fmtPhone('787-555-0142')")==='(787) 555-0142','tel desde seed: '+E("fmtPhone('787-555-0142')"));
ok(E("fmtPhone('+17875550142')")==='(787) 555-0142','tel con +1');
ok(E("fmtPhone('12345')")==='12345','tel que no calza queda igual');
ok(E("normPhone('(787) 555-0142')")==='7875550142','normaliza');
ok(E("normPhone('ext 5')")==='ext 5','no valido queda');
// cambio de formatos
E("setFmt('dateFmt','DMY');setFmt('dateSep','-')");
ok(E("fmtDate('2026-10-02')")==='02-10-2026','fecha DMY con -');
E("setFmt('dateFmt','YMD')");ok(E("fmtDate('2026-10-02')")==='2026-10-02','YMD');
E("setFmt('thou','.');setFmt('dec',',')");
ok(E("fmtMoney(1234.5)")==='$1.234,50','money europeo');
E("setFmt('symPos','after')");ok(E("fmtMoney(1234.5)")==='1.234,50 $','simbolo despues: '+E("fmtMoney(1234.5)"));
// separadores iguales se rechazan
E("setFmt('dec','.')");ok(E("TENANT_FMT.thou")===',','al escoger decimal igual al de miles, el de miles se intercambia');
ok(E("TENANT_FMT.thou!==TENANT_FMT.dec"),'separadores siempre distintos');
// restaurar region
E("setRegion('PR')");ok(E("regionIsCustom()")===false,'region restaurada no es custom');
ok(E("fmtMoney(1234.5)")==='$1,234.50','money tras restaurar');
E("setFmt('time','24')");ok(/^\d{2}:\d{2}$/.test(E("fmtTime(new Date(Date.UTC(2026,9,2,18,5)))")),'hora 24: '+E("fmtTime(new Date(Date.UTC(2026,9,2,18,5)))"));
ok(E("fmtTime(new Date(Date.UTC(2026,9,2,18,5)))")==='14:05','18:05Z en Puerto Rico = 14:05');
E("setFmt('time','12')");ok(E("fmtTimeStr('14:05')")==='2:05 p. m.'||E("fmtTimeStr('14:05')").includes('2:05'),'timeStr 12h');
// "hoy" en la zona, no UTC
E("setRegion('PR')");
const r=E("(()=>{const real=Date;const fixed=new real(Date.UTC(2026,9,3,1,30));global=null;return todayParts(fixed)})()");
ok(r.d==='02'&&r.m==='10','hoy a la 1:30Z del 3-oct = 2-oct en Puerto Rico: '+JSON.stringify(r));
E("setFmt('tz','Pacific/Honolulu')");const r2=E("todayParts(new Date(Date.UTC(2026,9,3,1,30)))");ok(r2.d==='02','Honolulu tambien 2-oct');
E("setFmt('tz','America/Puerto_Rico')");
ok(/^\d{4}-\d{2}-\d{2}$/.test(E("TODAY")),'TODAY ISO');
ok(E("isoDaysAgo(1)<TODAY"),'isoDaysAgo');
// regiones
ok(E("Object.keys(REGIONS).join()")==='PR,US','solo PR y US');
E("setRegion('US')");ok(E("TENANT_FMT.tz")==='America/New_York','US zona NY');E("setRegion('PR')");
// pantallas de ajustes: cada pestaña sin errores
for(const tab of ['general','region','calendar','modules','ops','brand']){
 E(`setSetTab('${tab}')`);const html=w.document.getElementById('wrap').innerHTML;
 ok(html.length>500&&!/undefined|NaN/.test(html.replace(/\bundefined\b[^<]{0,0}/g,'')),`pestaña ${tab} renderiza sin undefined/NaN`);
}
E("go('ajustes')");
ok(errs.length===0,'sin errores en pestañas: '+errs.join('|'));
done();
