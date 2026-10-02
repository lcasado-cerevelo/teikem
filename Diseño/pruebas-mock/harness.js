const {JSDOM}=require('jsdom');const fs=require('fs');
exports.load=function(){
 const html=fs.readFileSync(require('path').join(__dirname,'..','teikem-mockups.html'),'utf8');
 const errs=[];
 const dom=new JSDOM(html,{runScripts:'dangerously',pretendToBeVisual:true,url:'http://localhost/',
  beforeParse(w){w.addEventListener('error',e=>errs.push(String(e.message)));w.scrollTo=()=>{};w.HTMLElement.prototype.scrollIntoView=()=>{};
   w.matchMedia=w.matchMedia||(()=>({matches:false,addListener(){},removeListener(){},addEventListener(){}}));
   w.speechSynthesis={cancel(){},speak(){}};}});
 return {w:dom.window,errs};
};
let pass=0,fail=0;
exports.ok=(c,m)=>{if(c){pass++}else{fail++;console.log('FAIL:',m)}};
exports.done=()=>{console.log(`${pass} ok, ${fail} fail`);process.exit(fail?1:0)};
