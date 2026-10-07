/* NSQ_RRHH_EDITOR_COMPACTO_PDF_MODAL_V2_5 */
document.addEventListener('DOMContentLoaded', () => {
    const root = document.querySelector('.rrhh20');
    if (!root) return;

    const editable = root.dataset.editable === 'true';
    const escalaId = Number(root.dataset.escalaId || 0);

    const form = document.getElementById('rrhh20BulkForm');
    const functionSelect = document.getElementById('rrhh20Function');
    const turnSelect = document.getElementById('rrhh20Turn');
    const machineSelect = document.getElementById('rrhh20Machine');
    const functionHidden = document.getElementById('rrhh20FunctionHidden');
    const turnHidden = document.getElementById('rrhh20TurnHidden');
    const machineHidden = document.getElementById('rrhh20MachineHidden');
    const search = document.getElementById('rrhh20Search');
    const selectedCount = document.getElementById('rrhh20SelectedCount');
    const selectVisible = document.getElementById('rrhh20SelectVisible');
    const clearButton = document.getElementById('rrhh20Clear');
    const copyButton = document.getElementById('rrhh20CopyPrevious');
    const applyButton = document.getElementById('rrhh20Apply');
    const actionTitle = document.getElementById('rrhh20ActionTitle');
    const actionHelp = document.getElementById('rrhh20ActionHelp');
    const peopleGrid = document.getElementById('rrhh20PeopleGrid');
    const people = [...root.querySelectorAll('[data-person]')];

    const openPdf = document.getElementById('rrhh20OpenPdf');
    const pdfModal = document.getElementById('rrhh20PdfModal');
    const pdfClose = document.getElementById('rrhh20PdfClose');
    const pdfFrame = document.getElementById('rrhh20PdfFrame');
    const pdfBody = pdfFrame?.closest('.rrhh20-pdf-body');
    const pdfDownload = document.getElementById('rrhh20PdfDownload');

    const token = form?.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const scrollKey = `rrhh20-scroll-${escalaId}`;
    const gridScrollKey = `rrhh20-grid-scroll-${escalaId}`;

    function normalize(value) {
        return (value || '')
            .toString()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g,'')
            .toLowerCase()
            .trim();
    }

    function checked() {
        return people
            .map(card => card.querySelector('input[type="checkbox"]'))
            .filter(input => input && input.checked);
    }

    function refreshCounter() {
        const count = checked().length;
        if (selectedCount) selectedCount.textContent = String(count);

        const fn = functionSelect?.selectedOptions?.[0]?.textContent?.trim() || '';
        const turno = turnSelect?.selectedOptions?.[0]?.textContent?.trim() || '';

        if (!actionTitle || !actionHelp) return;

        if (!fn || turnSelect?.value === '') {
            actionTitle.textContent = 'Selecciona función y horario';
            actionHelp.textContent = `${count} persona(s) seleccionada(s).`;
            return;
        }

        if (turnSelect.value === '0') {
            actionTitle.textContent = `Quitar horario a ${count} persona(s)`;
            actionHelp.textContent = 'Las personas quedarán sin turno en esta escala.';
            return;
        }

        actionTitle.textContent = `${fn} · ${turno}`;
        actionHelp.textContent = `Aplicar a ${count} persona(s) seleccionada(s).`;
    }

    function applySearch() {
        const q = normalize(search?.value);
        people.forEach(card => {
            const haystack = normalize(card.dataset.search || card.textContent);
            card.hidden = !!q && !haystack.includes(q);
        });
    }

    function syncHidden() {
        if (functionHidden) functionHidden.value = functionSelect?.value || '';
        if (turnHidden) turnHidden.value = turnSelect?.value || '';
        if (machineHidden) machineHidden.value = machineSelect?.value || '';

        const requiresMachine = functionSelect?.selectedOptions?.[0]?.dataset?.machine === 'true';
        if (machineSelect) {
            machineSelect.closest('label')?.classList.toggle('rrhh20-machine-required',requiresMachine);
        }
        refreshCounter();
    }

    // Evita el salto vertical que provocaba el checkbox visual oculto.
    // También conserva el scroll interno del directorio.
    let pointerPageY = null;
    let pointerGridY = null;
    people.forEach(card => {
        const input = card.querySelector('input[type="checkbox"]');
        if (!input) return;

        input.addEventListener('pointerdown',() => {
            pointerPageY = window.scrollY;
            pointerGridY = peopleGrid?.scrollTop ?? 0;
        });

        input.addEventListener('change',() => {
            const pageY = pointerPageY;
            const gridY = pointerGridY;
            refreshCounter();
            requestAnimationFrame(() => {
                if (peopleGrid && gridY !== null) peopleGrid.scrollTop = gridY;
                if (pageY !== null && Math.abs(window.scrollY - pageY) > 2) {
                    window.scrollTo({ top: pageY, left: 0, behavior: 'auto' });
                }
                pointerPageY = null;
                pointerGridY = null;
            });
        });
    });

    search?.addEventListener('input',applySearch);
    functionSelect?.addEventListener('change',syncHidden);
    turnSelect?.addEventListener('change',syncHidden);
    machineSelect?.addEventListener('change',syncHidden);

    selectVisible?.addEventListener('click',() => {
        if (!editable) return;
        const visible = people.filter(card => !card.hidden);
        const shouldCheck = visible.some(card => {
            const input = card.querySelector('input[type="checkbox"]');
            return input && !input.checked;
        });
        visible.forEach(card => {
            const input = card.querySelector('input[type="checkbox"]');
            if (input) input.checked = shouldCheck;
        });
        refreshCounter();
    });

    clearButton?.addEventListener('click',() => {
        people.forEach(card => {
            const input = card.querySelector('input[type="checkbox"]');
            if (input) input.checked = false;
        });
        if (search) search.value = '';
        applySearch();
        refreshCounter();
    });

    form?.addEventListener('submit',async event => {
        event.preventDefault();
        if (!editable || !form) return;

        syncHidden();
        const selected = checked();

        if (!selected.length) {
            alert('Selecciona por lo menos una persona.');
            return;
        }
        if (!functionSelect?.value && turnSelect?.value !== '0') {
            alert('Selecciona el tipo de personal.');
            return;
        }
        if (turnSelect?.value === '') {
            alert('Selecciona un horario.');
            return;
        }

        const data = new FormData(form);
        data.delete('PersonalIDs');
        selected.forEach(input => data.append('PersonalIDs',input.value));

        root.classList.add('rrhh20-loading');
        if (applyButton) applyButton.disabled = true;

        try {
            const response = await fetch(form.action,{
                method:'POST',body:data,credentials:'same-origin',
                headers:{'X-Requested-With':'XMLHttpRequest','Accept':'application/json'}
            });
            const result = await response.json();
            if (!response.ok || !result?.ok)
                throw new Error(result?.mensaje || 'No fue posible guardar la programación.');

            sessionStorage.setItem(scrollKey,String(window.scrollY));
            sessionStorage.setItem(gridScrollKey,String(peopleGrid?.scrollTop ?? 0));
            window.location.reload();
        }
        catch (error) {
            alert(error?.message || 'No fue posible guardar la programación.');
        }
        finally {
            root.classList.remove('rrhh20-loading');
            if (applyButton) applyButton.disabled = false;
        }
    });

    copyButton?.addEventListener('click',async () => {
        if (!editable) return;
        if (!confirm('Esto reemplazará las asignaciones actuales con la escala anterior. ¿Continuar?')) return;

        const data = new FormData();
        data.append('__RequestVerificationToken',token);
        data.append('escalaID',String(escalaId));

        root.classList.add('rrhh20-loading');
        copyButton.disabled = true;
        try {
            const response = await fetch('/EscalaPersonal/CopiarSemanaAnteriorV20',{
                method:'POST',body:data,credentials:'same-origin',
                headers:{'X-Requested-With':'XMLHttpRequest','Accept':'application/json'}
            });
            const result = await response.json();
            if (!response.ok || !result?.ok)
                throw new Error(result?.mensaje || 'No fue posible copiar la semana anterior.');

            sessionStorage.setItem(scrollKey,String(window.scrollY));
            sessionStorage.setItem(gridScrollKey,String(peopleGrid?.scrollTop ?? 0));
            window.location.reload();
        }
        catch (error) {
            alert(error?.message || 'No fue posible copiar la semana anterior.');
        }
        finally {
            root.classList.remove('rrhh20-loading');
            copyButton.disabled = false;
        }
    });

    function closePdfModal() {
        if (!pdfModal) return;
        pdfModal.classList.remove('is-open');
        pdfModal.setAttribute('aria-hidden','true');
        document.body.classList.remove('rrhh20-modal-open');
    }

    openPdf?.addEventListener('click',() => {
        if (!pdfModal || !pdfFrame) return;
        const previewUrl = openPdf.dataset.previewUrl || '';
        const downloadUrl = openPdf.dataset.downloadUrl || '';

        if (pdfDownload) pdfDownload.href = downloadUrl || '#';
        pdfModal.classList.add('is-open');
        pdfModal.setAttribute('aria-hidden','false');
        document.body.classList.add('rrhh20-modal-open');

        if (!pdfFrame.getAttribute('src') && previewUrl) {
            pdfBody?.classList.remove('ready');
            pdfFrame.src = previewUrl;
        }
    });

    pdfFrame?.addEventListener('load',() => pdfBody?.classList.add('ready'));
    pdfClose?.addEventListener('click',closePdfModal);
    pdfModal?.addEventListener('click',event => {
        if (event.target === pdfModal) closePdfModal();
    });
    document.addEventListener('keydown',event => {
        if (event.key === 'Escape' && pdfModal?.classList.contains('is-open')) closePdfModal();
    });

    // Predeterminado: OPERADOR cuando exista.
    if (functionSelect && !functionSelect.value) {
        const operator = [...functionSelect.options].find(
            option => normalize(option.dataset.name) === 'operador'
        );
        if (operator) functionSelect.value = operator.value;
    }

    syncHidden();
    applySearch();
    refreshCounter();

    // Restaura exactamente la posición tras aplicar horarios/copiar semana.
    const savedY = Number(sessionStorage.getItem(scrollKey) || '');
    const savedGrid = Number(sessionStorage.getItem(gridScrollKey) || '');
    sessionStorage.removeItem(scrollKey);
    sessionStorage.removeItem(gridScrollKey);
    requestAnimationFrame(() => {
        if (Number.isFinite(savedY) && savedY >= 0) window.scrollTo(0,savedY);
        if (peopleGrid && Number.isFinite(savedGrid) && savedGrid >= 0) peopleGrid.scrollTop = savedGrid;
    });
});
