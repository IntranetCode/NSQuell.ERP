// NSQ_RH_ARRASTRE_HISTORIAL_V1_1
document.addEventListener('DOMContentLoaded', () => {
    'use strict';
    const root = document.querySelector('.rrhh20');
    if (!root) return;
    const editable = root.dataset.editable === 'true';
    const escalaId = root.dataset.escalaId;
    const form = document.getElementById('rhDragSave');
    const token = form?.querySelector('[name="__RequestVerificationToken"]')?.value;
    const catalog = document.getElementById('rhCatalog');
    const persons = new Map([...catalog.querySelectorAll('[data-rh-person]')].map(x => [x.dataset.rhPerson, x]));
    const selected = new Set();
    const status = document.getElementById('rhStatus');
    const search = document.getElementById('rhSearch');
    const pdfFrame = document.getElementById('rrhh20PdfFrame');
    let busy = false, dragged = [];
    const normalize = value => (value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();

    function message(text, error = false) {
        status.textContent = text;
        status.classList.toggle('is-error', error);
    }
    function filter() {
        const q = normalize(search.value);
        let visible = 0;
        persons.forEach(card => {
            // Conservar la ficha en el mapa permite moverla desde cualquier celda.
            card.hidden = card.dataset.assigned === 'true' ||
                (!!q && !normalize(card.dataset.search).includes(q));
            if (!card.hidden) visible++;
        });
        const empty = document.getElementById('rhNoPeople');
        empty.hidden = visible > 0;
        empty.textContent = q ? 'No hay personas sin horario que coincidan con la búsqueda.'
            : persons.size ? 'Todas las personas disponibles ya tienen horario. Puedes moverlas directamente en la tabla.'
            : 'No hay personal activo disponible en esta escala.';
    }
    function refresh() {
        root.querySelectorAll('[data-rh-person]').forEach(card => {
            const active = selected.has(card.dataset.rhPerson);
            card.classList.toggle('is-selected', active);
            card.setAttribute('aria-pressed', String(active));
        });
        root.querySelectorAll('[data-rh-assign]').forEach(b => { b.disabled = !editable || busy || selected.size === 0 || b.closest('[data-rh-drop]').dataset.disabled === 'true'; });
        root.querySelectorAll('.rh-cell-people').forEach(cell => {
            const count = cell.querySelectorAll('[data-rh-person]').length;
            cell.closest('[data-rh-drop]').querySelector('[data-rh-count]').textContent = count;
            cell.closest('[data-rh-drop]').classList.toggle('has-people', count > 0);
        });
        const assigned = new Set([...root.querySelectorAll('.rh-cell-people [data-rh-person]')].map(x => x.dataset.rhPerson));
        document.getElementById('rhAssignedCount').textContent = assigned.size;
        const publish = document.getElementById('rhPublish');
        if (publish) publish.disabled = busy || assigned.size === 0;
        filter();
    }
    function loading(value) {
        busy = value;
        root.classList.toggle('rh-busy', value);
        root.setAttribute('aria-busy', String(value));
        refresh();
    }
    async function post(url, data) {
        const response = await fetch(url, {
            method: 'POST', body: data, credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' }
        });
        if (response.redirected || response.status === 401 || response.status === 403)
            throw new Error('La sesión terminó o no tienes permiso. Actualiza la página para continuar.');
        if (!(response.headers.get('content-type') || '').includes('application/json'))
            throw new Error('No se pudo confirmar el guardado. Actualiza la página antes de continuar.');
        const result = await response.json();
        if (!response.ok || !result.ok) throw new Error(result.mensaje || 'No fue posible guardar el horario.');
        return result;
    }
    async function assign(ids, target) {
        if (!editable || busy || !target || !form || !token || target.dataset.disabled === 'true') return;
        ids = [...new Set(ids)].filter(id => persons.has(id));
        if (!ids.length) return;
        const data = new FormData();
        data.append('__RequestVerificationToken', token);
        data.append('EscalaID', escalaId);
        data.append('FuncionID', target.dataset.function);
        data.append('EscalaTurnoID', target.dataset.turn);
        ids.forEach(id => data.append('PersonalIDs', id));
        loading(true);
        message('Guardando horario…');
        try {
            const result = await post(form.action, data);
            if (!Array.isArray(result.personalIDs))
                throw new Error('El servidor guardó el horario, pero no devolvió las personas confirmadas. Actualiza la página.');
            const accepted = new Set(result.personalIDs.map(String).filter(id => ids.includes(id)));
            root.querySelectorAll('.rh-cell-people [data-rh-person]').forEach(card => {
                if (accepted.has(card.dataset.rhPerson)) card.remove();
            });
            accepted.forEach(id => {
                const source = persons.get(id);
                const assigned = target.dataset.turn !== '0';
                source.dataset.assigned = String(assigned);
                source.querySelector('.rh-current').textContent = assigned ? target.dataset.label : 'Sin horario asignado';
                if (assigned) {
                    const card = source.cloneNode(true);
                    card.hidden = false;
                    card.classList.add('rh-assigned');
                    card.classList.remove('is-selected', 'is-dragging');
                    card.querySelector('.rh-current')?.remove();
                    target.querySelector('.rh-cell-people').appendChild(card);
                }
                selected.delete(id);
            });
            // La siguiente vista previa debe consultar la escala guardada, no el PDF anterior.
            pdfFrame?.removeAttribute('src');
            message(result.mensaje + (accepted.size < ids.length ? ' Algunas personas ya no están activas; actualiza el directorio.' : ''));
        }
        catch (error) { message(error.message || 'No se pudo confirmar el guardado. Actualiza la página.', true); }
        finally { loading(false); }
    }

    root.addEventListener('click', event => {
        const assignButton = event.target.closest('[data-rh-assign]');
        if (assignButton) { assign([...selected], assignButton.closest('[data-rh-drop]')); return; }
        const card = event.target.closest('[data-rh-person]');
        if (!card || !editable || busy || card.disabled || !persons.has(card.dataset.rhPerson)) return;
        const id = card.dataset.rhPerson;
        if (selected.has(id)) selected.delete(id); else selected.add(id);
        refresh();
        message(selected.size ? `${selected.size} persona(s) seleccionada(s). Pulsa “Asignar aquí” en su horario o arrastra la selección.` : 'Arrastra una persona al horario que le corresponde.');
    });
    root.addEventListener('dragstart', event => {
        const card = event.target.closest('[data-rh-person]');
        if (!card || !editable || busy || card.disabled || !persons.has(card.dataset.rhPerson)) { event.preventDefault(); return; }
        dragged = selected.has(card.dataset.rhPerson) ? [...selected] : [card.dataset.rhPerson];
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('application/x-nsq-rh-personal', JSON.stringify(dragged));
        card.classList.add('is-dragging');
    });
    root.querySelectorAll('[data-rh-drop]').forEach(target => {
        target.addEventListener('dragover', event => {
            if (!editable || busy || !dragged.length || target.dataset.disabled === 'true') return;
            event.preventDefault();event.dataTransfer.dropEffect = 'move';target.classList.add('is-over');
        });
        target.addEventListener('dragleave', event => {
            if (!target.contains(event.relatedTarget)) target.classList.remove('is-over');
        });
        target.addEventListener('drop', event => {
            event.preventDefault();target.classList.remove('is-over');
            const ids = [...dragged];dragged = [];assign(ids, target);
        });
    });
    root.addEventListener('dragend', () => {
        dragged = [];
        root.querySelectorAll('.is-over,.is-dragging').forEach(x => x.classList.remove('is-over', 'is-dragging'));
    });
    root.addEventListener('submit', event => { if (busy) event.preventDefault(); }, true);
    search.addEventListener('input', filter);
    document.getElementById('rhClearSelection').addEventListener('click', () => {
        if (busy) return;
        selected.clear();refresh();message('Selección limpiada.');
    });
    document.getElementById('rrhh20CopyPrevious')?.addEventListener('click', async () => {
        if (!editable || busy || !token) return;
        if (!confirm('Se reemplazarán los horarios de esta semana por los de la semana anterior. ¿Continuar?')) return;
        const data = new FormData();data.append('__RequestVerificationToken', token);data.append('escalaID', escalaId);
        loading(true);message('Copiando semana anterior…');
        try { await post('/EscalaPersonal/CopiarSemanaAnteriorV20', data);window.location.reload(); }
        catch (error) { message(error.message, true);loading(false); }
    });

    const openPdf = document.getElementById('rrhh20OpenPdf');
    const pdfModal = document.getElementById('rrhh20PdfModal');
    const pdfBody = pdfFrame?.closest('.rrhh20-pdf-body');
    function closePdf() {
        pdfModal?.classList.remove('is-open');pdfModal?.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('rrhh20-modal-open');
    }
    openPdf?.addEventListener('click', () => {
        if (busy || !pdfModal || !pdfFrame) return;
        document.getElementById('rrhh20PdfDownload').href = openPdf.dataset.downloadUrl || '#';
        pdfModal.classList.add('is-open');pdfModal.setAttribute('aria-hidden', 'false');document.body.classList.add('rrhh20-modal-open');
        if (!pdfFrame.getAttribute('src')) { pdfBody?.classList.remove('ready');pdfFrame.src = openPdf.dataset.previewUrl; }
    });
    pdfFrame?.addEventListener('load', () => pdfBody?.classList.add('ready'));
    document.getElementById('rrhh20PdfClose')?.addEventListener('click', closePdf);
    pdfModal?.addEventListener('click', event => { if (event.target === pdfModal) closePdf(); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') closePdf(); });
    refresh();
});
