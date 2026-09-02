(function(){
  'use strict';
  const form=document.getElementById('hccCaptureForm');
  if(!form)return;

  const selector=document.getElementById('hccCavitySelector');
  const hidden=document.getElementById('hccCavidadesValue');
  const count=document.getElementById('hccCavityCount');
  const summary=document.getElementById('hccFormSummary');
  const addInput=document.getElementById('hccNewCavity');
  const addButton=document.getElementById('hccAddCavity');

  const getToggles=()=>[...document.querySelectorAll('.hcc-cavity-toggle')];
  const selected=()=>getToggles().filter(x=>x.checked).map(x=>Number(x.value)).filter(Number.isFinite).sort((a,b)=>a-b);

  function parseFlexible(v){
    const s=(v||'').trim().replace(/\s/g,'').replace(',','.');
    if(!s)return NaN;
    return Number(s);
  }

  function syncNumericControl(ctrl){
    const input=ctrl.querySelector('.hcc-value-input');
    const badge=ctrl.querySelector('.hcc-auto-result');
    const result=ctrl.querySelector('.hcc-auto-result-input');
    if(!input||!badge)return;
    const min=ctrl.dataset.min===''?null:Number(ctrl.dataset.min);
    const max=ctrl.dataset.max===''?null:Number(ctrl.dataset.max);
    const val=parseFlexible(input.value);
    badge.classList.remove('ok','nok');
    if(Number.isNaN(val)){
      badge.textContent='—';
      if(result)result.value='';
      return;
    }
    const ok=(min===null||val>=min)&&(max===null||val<=max);
    badge.textContent=ok?'OK':'NOK';
    badge.classList.add(ok?'ok':'nok');
    if(result)result.value=ok?'OK':'NOK';
  }

  function initValueControl(ctrl){
    if(!ctrl||ctrl.dataset.hccInit==='1')return;
    ctrl.dataset.hccInit='1';
    const input=ctrl.querySelector('.hcc-value-input');
    const badge=ctrl.querySelector('.hcc-auto-result');
    if(!input||!badge)return;
    input.addEventListener('input',()=>syncNumericControl(ctrl));
    // V4: los valores dimensionales llegan precargados. Evalúalos desde el inicio.
    syncNumericControl(ctrl);
  }

  function initNumericControls(root=document){
    root.querySelectorAll('.hcc-value-control').forEach(initValueControl);
  }

  function setTriState(group,value){
    const hiddenResult=group.querySelector('.hcc-tri-result');
    const normalized=['OK','NOK','NA'].includes(value)?value:'OK';
    if(hiddenResult)hiddenResult.value=normalized;
    group.querySelectorAll('.hcc-tri-option').forEach(btn=>{
      btn.classList.toggle('active',btn.dataset.value===normalized);
      btn.setAttribute('aria-pressed',btn.dataset.value===normalized?'true':'false');
    });

    if(group.dataset.rowState==='1'){
      const row=group.closest('.hcc-check-row');
      row?.classList.toggle('hcc-check-row-ok',normalized==='OK');
      row?.classList.toggle('hcc-check-row-nok',normalized==='NOK');
      row?.classList.toggle('hcc-check-row-na',normalized==='NA');
    }
  }

  function initTriState(group){
    if(!group||group.dataset.hccTriInit==='1')return;
    group.dataset.hccTriInit='1';
    const hiddenResult=group.querySelector('.hcc-tri-result');
    const initial=(hiddenResult?.value||'OK').toUpperCase();
    group.querySelectorAll('.hcc-tri-option').forEach(btn=>{
      btn.addEventListener('click',()=>setTriState(group,(btn.dataset.value||'OK').toUpperCase()));
    });
    setTriState(group,initial);
  }

  function initTriStates(root=document){
    root.querySelectorAll('[data-hcc-tri]').forEach(initTriState);
  }

  function bindToggle(toggle){
    if(toggle.dataset.hccBound==='1')return;
    toggle.dataset.hccBound='1';
    toggle.addEventListener('change',()=>{
      if(selected().length===0)toggle.checked=true;
      applyCavities();
    });
  }

  function applyCavities(){
    const values=selected();
    hidden.value=values.join(',');
    count.textContent=String(values.length);
    summary.textContent=`${values.length} cavidad${values.length===1?'':'es'}/posiciones · 3 tiros`;

    getToggles().forEach(x=>{
      x.closest('.hcc-cavity-chip')?.classList.toggle('active',x.checked);
    });

    document.querySelectorAll('.hcc-cavity-row').forEach(row=>{
      const cav=Number(row.dataset.cavity);
      const visible=values.includes(cav);
      row.hidden=!visible;
      row.querySelectorAll('input,select,textarea,button').forEach(el=>{el.disabled=!visible;});
    });
  }

  function addRowsForCavity(cavity){
    document.querySelectorAll('.hcc-measure-card').forEach(card=>{
      const body=card.querySelector('[data-hcc-cavity-body]');
      const template=card.querySelector('.hcc-cavity-row-template');
      if(!body||!template)return;
      if(body.querySelector(`.hcc-cavity-row[data-cavity="${cavity}"]`))return;

      const carId=Number(card.dataset.characteristic||0);
      if(!carId)return;
      const baseToken=`c${carId}_v${cavity}_t`;
      const html=template.innerHTML
        .replaceAll('__CAV__',String(cavity))
        .replaceAll('__TOKEN__',baseToken);

      const holder=document.createElement('tbody');
      holder.innerHTML=html.trim();
      const row=holder.firstElementChild;
      if(!row)return;
      body.appendChild(row);
      initNumericControls(row);
      initTriStates(row);
    });
  }

  function addCavity(){
    const cavity=Number(addInput?.value||0);
    if(!Number.isInteger(cavity)||cavity<1||cavity>999){
      alert('Captura una cavidad/posición válida entre 1 y 999.');
      addInput?.focus();
      return;
    }
    if(selected().length>=64){
      alert('La captura admite como máximo 64 cavidades/posiciones.');
      return;
    }

    const existing=getToggles().find(x=>Number(x.value)===cavity);
    if(existing){
      existing.checked=true;
      applyCavities();
      existing.closest('.hcc-cavity-chip')?.scrollIntoView({behavior:'smooth',block:'nearest',inline:'center'});
      if(addInput)addInput.value='';
      return;
    }

    addRowsForCavity(cavity);

    const label=document.createElement('label');
    label.className='hcc-cavity-chip active hcc-cavity-chip-added';
    label.dataset.cavityChip=String(cavity);
    label.innerHTML=`<input type="checkbox" class="hcc-cavity-toggle" value="${cavity}" checked /><span>${cavity}</span><em>Nueva</em>`;
    selector?.appendChild(label);
    const toggle=label.querySelector('.hcc-cavity-toggle');
    if(toggle)bindToggle(toggle);
    if(addInput)addInput.value='';
    applyCavities();
  }

  getToggles().forEach(bindToggle);
  addButton?.addEventListener('click',addCavity);
  addInput?.addEventListener('keydown',e=>{
    if(e.key==='Enter'){
      e.preventDefault();
      addCavity();
    }
  });

  form.addEventListener('submit',e=>{
    const values=selected();
    if(values.length===0){
      e.preventDefault();
      alert('Selecciona al menos una cavidad/posición configurada.');
      return;
    }
    hidden.value=values.join(',');

    const enabledNumeric=[...form.querySelectorAll('.hcc-cavity-row:not([hidden]) .hcc-value-input')].filter(x=>!x.disabled);
    const empty=enabledNumeric.find(x=>!String(x.value||'').trim());
    if(empty){
      e.preventDefault();
      empty.focus();
      alert('Completa todos los valores dimensionales visibles antes de guardar.');
      return;
    }

    const autoEmpty=[...form.querySelectorAll('.hcc-cavity-row:not([hidden]) .hcc-auto-result-input')]
      .filter(x=>!x.disabled && !String(x.value||'').trim());
    if(autoEmpty.length){
      e.preventDefault();
      alert('Hay mediciones dimensionales sin resultado calculado. Revisa los valores capturados.');
      return;
    }

    const tieneNok=[...form.querySelectorAll('.hcc-tri-result,.hcc-auto-result-input')]
      .some(x=>!x.disabled && String(x.value||'').toUpperCase()==='NOK');
    const obs=form.querySelector('textarea[name="Observaciones"]');
    if(tieneNok && obs && !String(obs.value||'').trim()){
      e.preventDefault();
      obs.focus();
      alert('Existe al menos un NOK. Captura la observación, defecto y/o solución antes de guardar.');
    }
  });

  initNumericControls();
  initTriStates();
  applyCavities();
})();
