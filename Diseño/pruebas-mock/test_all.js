const {load,ok,done}=require('./harness');
const {w,errs}=load();const E=c=>w.eval(c);
const keys=E("Object.keys(SCREENS)");
let n=0;
for(const lang of ['es','en']){
 E(`LANG='${lang}';applyStatic()`);
 for(const k of keys){
  try{E(`go('${k}')`);const h=w.document.getElementById('wrap').innerHTML;n++;
   ok(h.length>100,`${lang}/${k} vacío`);
   const bad=h.match(/>[^<]*\b(undefined|NaN|\[object Object\])\b[^<]*</);
   ok(!bad,`${lang}/${k} contiene ${bad&&bad[0].slice(0,80)}`);
  }catch(e){ok(false,`${lang}/${k} lanzó ${e.message}`)}
 }
}
ok(errs.length===0,'window.onerror: '+errs.join('|'));
console.log(n+' pantallas recorridas');
done();
