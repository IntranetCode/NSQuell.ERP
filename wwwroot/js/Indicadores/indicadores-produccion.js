// NSQ_INDICADORES_PRODUCCION_ANALITICO_V1_4
(() => {
    const q = (s, root = document) => root.querySelector(s);
    const qa = (s, root = document) => Array.from(root.querySelectorAll(s));
    const setText = (id, value, fallback = '—') => {
        const el = document.getElementById(id);
        if (!el) return;
        const text = value === null || value === undefined || String(value).trim() === '' ? fallback : String(value);
        el.textContent = text;
    };
    const num = value => { const n = Number(value); return Number.isFinite(n) ? n : 0; };
    const integer = value => Math.round(num(value)).toLocaleString('es-MX');
    const pct = value => `${num(value).toLocaleString('es-MX',{minimumFractionDigits:1,maximumFractionDigits:2})}%`;
    const duration = value => { const total=Math.max(0,Math.round(num(value))); const h=Math.floor(total/60); const m=total%60; return h>0?`${h} h ${String(m).padStart(2,'0')} min`:`${m} min`; };

    const wireTabs = (buttonSelector, panelAttr) => {
        qa(buttonSelector).forEach(btn => btn.addEventListener('click', () => {
            const value = btn.dataset[Object.keys(btn.dataset).find(k => k.toLowerCase().includes('tab'))];
            const container = btn.parentElement;
            qa('button', container).forEach(x => x.classList.toggle('active', x === btn));
            qa(`[${panelAttr}]`).forEach(panel => panel.classList.toggle('d-none', panel.getAttribute(panelAttr) !== value));
        }));
    };

    qa('[data-ind-person-tab]').forEach(btn => btn.addEventListener('click', () => {
        const value=btn.dataset.indPersonTab;
        qa('[data-ind-person-tab]').forEach(x=>x.classList.toggle('active',x===btn));
        qa('[data-ind-person-panel]').forEach(p=>p.classList.toggle('d-none',p.dataset.indPersonPanel!==value));
    }));
    qa('[data-ind-loss-tab]').forEach(btn => btn.addEventListener('click', () => {
        const value=btn.dataset.indLossTab;
        qa('[data-ind-loss-tab]').forEach(x=>x.classList.toggle('active',x===btn));
        qa('[data-ind-loss-panel]').forEach(p=>p.classList.toggle('d-none',p.dataset.indLossPanel!==value));
    }));
    qa('[data-ind-paros-modal-tab]').forEach(btn => btn.addEventListener('click', () => {
        const value=btn.dataset.indParosModalTab;
        qa('[data-ind-paros-modal-tab]').forEach(x=>x.classList.toggle('active',x===btn));
        qa('[data-ind-paros-modal-panel]').forEach(p=>p.classList.toggle('d-none',p.dataset.indParosModalPanel!==value));
    }));

    qa('[data-ind-person-detail]').forEach(row => {
        row.addEventListener('click', () => {
            const d=row.dataset;
            const role=(d.role||'PERSONAL').toUpperCase();
            setText('indPersonRole', role==='TECNICO'?'TÉCNICO EN PRODUCCIÓN':role==='AUXILIAR'?'AUXILIAR DE PRODUCCIÓN':'OPERADOR');
            setText('indPersonModalTitle', d.name);
            setText('indPersonSubtitle', d.control ? `Control ${d.control} · ${integer(d.registros)} registros hora` : `${integer(d.registros)} registros hora`);
            setText('indPersonOee',pct(d.oee)); setText('indPersonRqt',pct(d.rqt)); setText('indPersonRql',pct(d.rql)); setText('indPersonUe',pct(d.ue)); setText('indPersonParos',pct(d.paros)); setText('indPersonScrapPct',pct(d.scrap));
            setText('indPersonOk',integer(d.ok)); setText('indPersonObjetivo',integer(d.objetivo)); setText('indPersonSospechosas',integer(d.sospechosas)); setText('indPersonScrap',integer(d.scrappiezas)); setText('indPersonTiempo',duration(d.minutos)); setText('indPersonTiempoParo',duration(d.minutosparo));
            if(role==='TECNICO'){
                setText('indPersonTraceRole','Produccion_Ejecucion.TecnicoProduccionID');
                setText('indPersonTraceRoleDetail','La participación se atribuye a las ejecuciones donde fue confirmado como técnico real.');
                setText('indPersonTraceParos','Produccion_Paros → Produccion_Ejecucion');
                setText('indPersonTraceParosDetail','Los paros se atribuyen a las mismas ejecuciones atendidas por el técnico.');
            } else if(role==='AUXILIAR'){
                setText('indPersonTraceRole','Produccion_Ejecucion.OperadorAuxiliarID');
                setText('indPersonTraceRoleDetail','La participación se atribuye a las ejecuciones donde fue confirmado como auxiliar real.');
                setText('indPersonTraceParos','Produccion_Paros → Produccion_Ejecucion');
                setText('indPersonTraceParosDetail','Los paros se atribuyen a las mismas ejecuciones atendidas por el auxiliar.');
            } else {
                setText('indPersonTraceRole','Produccion_RegistroHora.OperadorID');
                setText('indPersonTraceRoleDetail','La producción se atribuye al operador registrado en cada bloque horario.');
                setText('indPersonTraceParos','Produccion_Paros.OperadorID');
                setText('indPersonTraceParosDetail','Minutos de paro registrados directamente para el operador dentro del periodo.');
            }
        });
        row.addEventListener('keydown', e => { if(e.key==='Enter'||e.key===' '){ e.preventDefault(); row.click(); } });
    });

    qa('[data-ind-machine-detail]').forEach(row => row.addEventListener('click', () => {
        const d=row.dataset; setText('indMachineModalTitle',d.name); setText('indMachineOee',pct(d.oee)); setText('indMachineRqt',pct(d.rqt)); setText('indMachineRql',pct(d.rql)); setText('indMachineUe',pct(d.ue)); setText('indMachineParos',pct(d.paros)); setText('indMachineScrapPct',pct(d.scrap)); setText('indMachineOk',integer(d.ok)); setText('indMachineObjetivo',integer(d.objetivo)); setText('indMachineSospechosas',integer(d.sospechosas)); setText('indMachineScrap',integer(d.scrappiezas)); setText('indMachineTiempo',duration(d.minutos)); setText('indMachineTiempoParo',duration(d.minutosparo));
    }));

    qa('[data-ind-program-detail]').forEach(button => button.addEventListener('click', () => {
        const d=button.dataset;
        setText('indProgramModalTitle',d.of||`Programa ${d.programa||''}`); setText('indProgramSubtitle',`${d.parte||'Sin parte'} · ${d.maquina||'Sin máquina'} · Programa ${d.programa||'—'}`); setText('indModalObjetivoHora',num(d.objetivohora)>0?`${integer(d.objetivohora)} pzas/h`:'Sin dato'); setText('indModalCiclo',d.ciclo||'Sin dato'); setText('indModalCavidades',d.cavidades||'Sin dato'); setText('indModalFuente',d.fuente||'Sin estándar'); setText('indModalRqt',pct(d.rqt)); setText('indModalRql',pct(d.rql)); setText('indModalUe',pct(d.ue)); setText('indModalOee',pct(d.oee)); setText('indModalParosPct',pct(d.parospct)); setText('indModalScrapPct',pct(d.scrappct)); setText('indModalOk',integer(d.ok)); setText('indModalSospechosas',integer(d.sospechosas)); setText('indModalScrap',integer(d.scrap)); setText('indModalObjetivo',integer(d.objetivo)); setText('indModalTiempo',duration(d.minutos)); setText('indModalTiempoParo',duration(d.minutosparo));
    }));

    const parosModal=document.getElementById('indParosModal');
    qa('[data-ind-paro-focus]').forEach(btn=>btn.addEventListener('click',()=>{
        const index=btn.dataset.indParoFocus;
        q('[data-ind-paros-modal-tab="motivos"]')?.click();
        setTimeout(()=>{ qa('[data-ind-paro-modal-row]',parosModal).forEach(r=>r.classList.remove('is-focus')); const row=q(`[data-ind-paro-modal-row="${index}"]`,parosModal); row?.classList.add('is-focus'); row?.scrollIntoView({block:'nearest',behavior:'smooth'}); },180);
    }));
    qa('[data-ind-paro-maquina-focus]').forEach(btn=>btn.addEventListener('click',()=>{
        const id=btn.dataset.indParoMaquinaFocus;
        q('[data-ind-paros-modal-tab="maquinas"]')?.click();
        setTimeout(()=>{ qa('[data-ind-paro-maquina-row]',parosModal).forEach(r=>r.classList.remove('is-focus')); const row=q(`[data-ind-paro-maquina-row="${id}"]`,parosModal); row?.classList.add('is-focus'); row?.scrollIntoView({block:'nearest',behavior:'smooth'}); },180);
    }));
    parosModal?.addEventListener('hidden.bs.modal',()=>qa('.is-focus',parosModal).forEach(r=>r.classList.remove('is-focus')));
})();