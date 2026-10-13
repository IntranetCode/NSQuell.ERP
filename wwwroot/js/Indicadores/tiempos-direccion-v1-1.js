// NSQ_TIEMPOS_DIRECCION_V1_1. Indicadores de la consulta; relojes de etapas abiertas en pantalla.
(function(){
 'use strict';
 const panel=document.getElementById('tdx-panel');if(!panel)return;
 const form=document.getElementById('tdx-filter'), enfoque=form?.querySelector('[name="enfoque"]');
 function orientar(){const ahora=enfoque?.value==='AHORA';['desde','hasta','categoria','soloActivos'].forEach(name=>{const el=form?.querySelector('[name="'+name+'"]');if(el){el.disabled=ahora;el.closest('div,label')?.classList.toggle('tdx-filter-disabled',ahora);}});}
 enfoque?.addEventListener('change',orientar);form?.querySelector('[name="departamento"]')?.addEventListener('change',()=>{const fase=form.querySelector('[name="fase"]');if(fase)fase.value='';});orientar();
 const inicio=performance.now();
 function actualizar(){
  const delta=Math.max(0,(performance.now()-inicio)/1000);
  panel.querySelectorAll('[data-tdx-seconds]').forEach(el=>{
   const inicial=Number(el.dataset.tdxSeconds),max=Number(el.dataset.tdxMax);
   if(!Number.isFinite(inicial)||!Number.isFinite(max))return;
   const total=Math.floor(Math.max(0,Math.min(max,inicial+(el.dataset.tdxActive==='1'?delta:0))));
   el.textContent=Math.floor(total/3600).toLocaleString('es-MX')+' h '+String(Math.floor(total%3600/60)).padStart(2,'0')+' min';
  });
 }
 actualizar();setInterval(actualizar,1000);
 panel.querySelector('[data-tdx-print]')?.addEventListener('click',()=>window.print());
})();
