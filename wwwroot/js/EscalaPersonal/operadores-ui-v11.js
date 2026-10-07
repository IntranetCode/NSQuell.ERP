(() => {
    'use strict';
    const norm = value => (value || '').toString().normalize('NFD').replace(/[\u0300-\u036f]/g,'').toUpperCase().replace(/\s+/g,' ').trim();
    function apply() {
        document.querySelectorAll('a,button,input[type="submit"]').forEach(el => {
            const text = norm(el.tagName === 'INPUT' ? el.value : el.textContent);
            if (text.includes('RESTABLECER CONTRASENA')) el.classList.add('rh-op-action','rh-op-reset');
            if (text.includes('DAR DE BAJA')) el.classList.add('rh-op-action','rh-op-danger');
            if (text === 'CANCELAR' || text === 'REGRESAR' || text === 'VOLVER' || text.includes('REGRESAR A OPERADORES') || text.includes('VOLVER A OPERADORES')) el.classList.add('rh-op-action','rh-op-cancel');
            if (text.includes('CREAR OPERADOR') || text.includes('DAR DE ALTA') || text.includes('GUARDAR OPERADOR')) el.classList.add('rh-op-action','rh-op-primary');
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded',apply,{once:true}); else apply();
})();