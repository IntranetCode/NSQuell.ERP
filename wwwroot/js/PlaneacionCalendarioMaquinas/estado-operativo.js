// NSQ_CALENDARIOS_ESTADO_COMPARTIDO_V1_0
// Actualiza únicamente el semáforo; conserva formularios, modales y arrastre.
(function () {
    'use strict';
    const selector = '.block[data-programa-id][data-color-operativo]';
    const colores = ['bloque-timeline', 'bloque-produciendo', 'bloque-paro',
        'bloque-calidad', 'bloque-reliberacion', 'bloque-producido', 'bloque-cerrado'];
    let consultando = false;
    let arrastrando = false;

    function ocupada() {
        return document.hidden || arrastrando || !!document.querySelector('.modal.show');
    }

    async function actualizar() {
        if (consultando || ocupada() || !document.querySelector(selector)) return;
        consultando = true;
        try {
            const response = await fetch(window.location.href, {
                cache: 'no-store', credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok || response.redirected) return;
            const html = await response.text();
            if (ocupada()) return;
            const nuevo = new DOMParser().parseFromString(html, 'text/html');
            const estados = new Map();
            nuevo.querySelectorAll(selector).forEach(b => estados.set(b.dataset.programaId, b));
            document.querySelectorAll(selector).forEach(b => {
                const fuente = estados.get(b.dataset.programaId);
                if (!fuente || !colores.includes(fuente.dataset.colorOperativo)) return;
                b.classList.remove(...colores);
                b.classList.add(fuente.dataset.colorOperativo);
                b.dataset.colorOperativo = fuente.dataset.colorOperativo;
                b.title = fuente.title;
                const actual = b.querySelector('[data-estado-operativo-texto]');
                const estado = fuente.querySelector('[data-estado-operativo-texto]');
                if (actual && estado) actual.textContent = estado.textContent;
                b.classList.toggle('has-start-delay', fuente.classList.contains('has-start-delay'));
                const alerta = b.querySelector('.start-delay-alert');
                const nuevaAlerta = fuente.querySelector('.start-delay-alert');
                if (alerta && !nuevaAlerta) alerta.remove();
                else if (alerta && nuevaAlerta) alerta.title = nuevaAlerta.title;
                else if (!alerta && nuevaAlerta) b.prepend(nuevaAlerta.cloneNode(true));
                // Una OF iniciada/finalizada tampoco conserva permiso de arrastre obsoleto.
                b.draggable = fuente.draggable;
                b.dataset.puedeMover = fuente.dataset.puedeMover;
                b.dataset.motivoBloqueo = fuente.dataset.motivoBloqueo;
                b.classList.toggle('no-drag', fuente.classList.contains('no-drag'));
                if (estado) {
                    document.querySelectorAll('[data-semaforo-programa]').forEach(s => {
                        if (s.dataset.semaforoPrograma === b.dataset.programaId)
                            s.textContent = estado.textContent;
                    });
                }
            });
        }
        catch (error) {
            console.warn('No fue posible actualizar los estados de Planeación:', error);
        }
        finally { consultando = false; }
    }

    document.addEventListener('dragstart', e => {
        if (e.target.closest?.(selector)) arrastrando = true;
    });
    document.addEventListener('dragend', () => { arrastrando = false; });
    document.addEventListener('nsq:produccion-finalizada', actualizar);
    document.addEventListener('hidden.bs.modal', actualizar);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) actualizar(); });
    window.addEventListener('focus', actualizar);
    const timer = window.setInterval(actualizar, 30000);
    window.addEventListener('pagehide', () => window.clearInterval(timer));
})();
