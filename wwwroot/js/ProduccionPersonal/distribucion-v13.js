/* NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V14_3 */
(() => {
    'use strict';

    // NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V14
    const panel = document.querySelector('.ppv3-view[data-view-panel="planner"]');
    if (!panel) return;

    const api = {
        board: '/ProduccionPersonal/DistribucionV14',
        candidates: '/ProduccionPersonal/DistribucionV14/Candidatos',
        programs: '/ProduccionPersonal/DistribucionV14/Programas',
        parts: '/ProduccionPersonal/DistribucionV14/Partes',
        warning: '/ProduccionPersonal/DistribucionV14/Advertencia',
        save: '/ProduccionPersonal/DistribucionV14/Guardar',
        extra: '/ProduccionPersonal/DistribucionV14/Extra',
        pdf: '/ProduccionPersonal/DistribucionV14/Pdf'
    };

    const urlParams = new URLSearchParams(window.location.search);

    const state = {
        vista: (urlParams.get('vista') || 'dia').toLowerCase(),
        fechaDesde: urlParams.get('fechaDesde') || new Date().toISOString().slice(0, 10),
        fechaHasta: urlParams.get('fechaHasta') || '',
        semanaDesde: urlParams.get('semanaDesde') || '',
        turnoId: null,
        period: null,
        turns: [],
        rows: [],
        candidatesCache: new Map(),
        activePicker: null,
        change: null,
        pieceRow: null,
        pdfBlobUrl: null
    };

    const antiForgery =
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    const legacyHead = panel.querySelector('.ppv3-view-head');
    const legacyTable = panel.querySelector('.ppv3-table-wrap');
    legacyHead?.classList.add('ppv14-legacy-hidden');
    legacyTable?.classList.add('ppv14-legacy-hidden');

    const root = document.createElement('div');
    root.id = 'ppv14DistributionRoot';
    root.className = 'ppv14-board';
    panel.prepend(root);

    const escapeHtml = value => String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');

    const normalize = value => String(value ?? '')
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase();

    const centerKey = row =>
        row.esEspecial ? `special:${row.centroClave}` : `machine:${row.maquinaID}`;

    function queryForBoard(turnId = state.turnoId) {
        const q = new URLSearchParams();
        q.set('vista', state.vista);
        q.set('fechaDesde', state.fechaDesde);
        if (state.fechaHasta) q.set('fechaHasta', state.fechaHasta);
        if (state.semanaDesde) q.set('semanaDesde', state.semanaDesde);
        if (turnId) q.set('turnoId', String(turnId));
        return q;
    }

    function bodyPeriod(body) {
        body.append('vista', state.vista);
        body.append('fechaDesde', state.fechaDesde);
        if (state.fechaHasta) body.append('fechaHasta', state.fechaHasta);
        if (state.semanaDesde) body.append('semanaDesde', state.semanaDesde);
    }

    async function fetchJson(url, options) {
        const response = await fetch(url, {
            credentials: 'same-origin',
            ...options
        });

        const raw = await response.text();
        let data = null;

        if (raw) {
            try {
                data = JSON.parse(raw);
            } catch {
                data = null;
            }
        }

        if (!response.ok || data?.ok === false) {
            const message =
                data?.message ||
                data?.mensaje ||
                raw.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 500) ||
                `Error ${response.status}`;

            throw new Error(message);
        }

        return data;
    }

    function setLoading(message = 'Cargando distribución...') {
        root.innerHTML = `
            <div class="ppv14-loading">
                <div class="spinner-border spinner-border-sm" role="status"></div>
                <span>${escapeHtml(message)}</span>
            </div>`;
    }

    function periodDescription() {
        if (!state.period) return '';

        const days = Number(state.period.diasAplicacion || 1);

        if (state.period.vista === 'dia')
            return 'Los cambios aplican únicamente a este día.';

        if (state.period.vista === 'semana')
            return `Los cambios se replican en los ${days} días de la semana mostrada.`;

        if (state.period.vista === 'mes')
            return `Mes mostrado arriba. Estás editando la semana ${state.period.edicionDesde} a ${state.period.edicionHasta}.`;

        return `Los cambios se replican en los ${days} días del rango seleccionado.`;
    }

    function renderMonthWeeks() {
        if (state.period?.vista !== 'mes' || !state.period.semanasMes?.length)
            return '';

        return `
            <div class="ppv14-month-weeks">
                <span class="ppv14-field-label">Semana a programar dentro del mes</span>
                <div class="ppv14-week-list">
                    ${state.period.semanasMes.map(week => `
                        <button type="button"
                                class="ppv14-week-chip ${week.seleccionada ? 'active' : ''}"
                                data-week-start="${escapeHtml(week.desde)}">
                            <i class="fa-solid fa-calendar-week"></i>
                            <span>${escapeHtml(week.etiqueta)}</span>
                        </button>
                    `).join('')}
                </div>
            </div>`;
    }

    function turnButtons() {
        return state.turns.map(turn => `
            <button type="button"
                    class="ppv14-turn-chip ${Number(state.turnoId) === Number(turn.turnoID) ? 'active' : ''}"
                    data-turn-id="${turn.turnoID}"
                    style="--turn-color:${escapeHtml(turn.color || '#64748B')}">
                <strong>${escapeHtml(turn.nombre)}</strong>
                <small>${escapeHtml(turn.tipo)}</small>
            </button>
        `).join('');
    }

    function pieceControl(row) {
        if (row.esEspecial) {
            return `
                <div class="ppv14-piece-static">
                    <strong>TORNILLO</strong>
                    <small>Puesto especial</small>
                </div>`;
        }

        const title = row.numeroParte || row.referenciaSAP || 'Sin pieza programada';
        const subtitle = row.descripcionParte || 'Selecciona una pieza mientras se genera la OF';

        let tag = '';
        if (row.piezaProgramada) {
            tag = '<span class="ppv14-tag program"><i class="fa-solid fa-link"></i> Producción</span>';
        } else if (row.parteID) {
            tag = '<span class="ppv14-tag manual"><i class="fa-solid fa-pen"></i> Provisional</span>';
        }

        return `
            <button type="button"
                    class="ppv14-piece ${row.piezaProgramada ? 'programmed' : row.parteID ? 'manual' : ''}"
                    data-piece-open
                    data-key="${escapeHtml(centerKey(row))}">
                <div>
                    <strong>${escapeHtml(title)}</strong>
                    <small>${escapeHtml(subtitle)}</small>
                </div>
                <div class="ppv14-piece-side">
                    ${tag}
                    <i class="fa-solid fa-chevron-right"></i>
                </div>
            </button>`;
    }

    function personCard(person, extra = false) {
        const control = person.numeroControl || person.numeroControlOperador || '';
        const name = person.nombre || person.operadorNombre || '';

        return `
            <button type="button"
                    class="ppv14-person-chip ${extra ? 'extra' : 'primary'}"
                    ${extra ? `data-extra-edit="${person.extraID}" data-operator-id="${person.operadorID}"` : 'data-primary-edit'}
                    title="${extra ? 'Cambiar operador adicional' : 'Cambiar operador principal'}">
                <span class="ppv14-person-dot"></span>
                <span class="ppv14-person-text">
                    <strong>${escapeHtml(name)}</strong>
                    <small>${control ? `${escapeHtml(control)} · ` : ''}${extra ? 'Operador adicional' : 'Operador principal'}</small>
                </span>
                <i class="fa-solid fa-pen"></i>
            </button>`;
    }

    function operatorArea(row) {
        const primary = row.operadorID
            ? personCard({
                operadorNombre: row.operadorNombre,
                numeroControlOperador: row.numeroControlOperador
            })
            : `
                <div class="ppv14-inline-picker-host">
                    <button type="button"
                            class="ppv14-select-button"
                            data-picker-open="primary"
                            data-key="${escapeHtml(centerKey(row))}">
                        <i class="fa-solid fa-user-plus"></i>
                        <span>Seleccionar operador...</span>
                        <i class="fa-solid fa-chevron-down"></i>
                    </button>
                </div>`;

        const extras = (row.extras || []).map(extra => personCard(extra, true)).join('');

        const add =
            row.operadorID
                ? `
                    <button type="button"
                            class="ppv14-add-person"
                            data-picker-open="extra"
                            data-key="${escapeHtml(centerKey(row))}"
                            title="Agregar otra persona a esta máquina">
                        <i class="fa-solid fa-plus"></i>
                        <span>Agregar operador</span>
                    </button>`
                : '';

        return `
            <div class="ppv14-operator-stack">
                ${primary}
                ${extras}
                ${add}
            </div>`;
    }

    function renderWarningFromSession() {
        const warning = sessionStorage.getItem('ppv14-warning');
        if (!warning) return '';

        sessionStorage.removeItem('ppv14-warning');

        return `
            <div class="ppv14-warning-banner" id="ppv14WarningBanner">
                <i class="fa-solid fa-triangle-exclamation"></i>
                <div>
                    <strong>Asignación guardada con advertencia</strong>
                    <span>${escapeHtml(warning)}</span>
                </div>
                <button type="button" data-dismiss-warning aria-label="Cerrar">
                    <i class="fa-solid fa-xmark"></i>
                </button>
            </div>`;
    }

    function render() {
        const assignedPrimary = state.rows.filter(x => x.operadorID).length;
        const extraCount = state.rows.reduce((sum, x) => sum + (x.extras?.length || 0), 0);
        const pending = state.rows.length - assignedPrimary;

        root.innerHTML = `
            ${renderWarningFromSession()}

            <section class="ppv14-hero">
                <div class="ppv14-hero-copy">
                    <span class="ppv14-eyebrow">DISTRIBUCIÓN DE PERSONAL</span>
                    <h2>Operadores por máquina y turno</h2>
                    <p>
                        La programación se guarda según el periodo elegido arriba.
                        La pieza puede definirse antes de tener OF; cuando Producción tenga un programa real,
                        esa referencia pasa a ser la efectiva.
                    </p>
                </div>

                <div class="ppv14-hero-actions">
                    <button type="button" class="ppv14-action secondary" id="ppv14LegacyToggle">
                        <i class="fa-solid fa-list"></i>
                        <span>Ver detalle por OF</span>
                    </button>

                    <button type="button" class="ppv14-action primary" id="ppv14PdfOpen">
                        <i class="fa-solid fa-file-pdf"></i>
                        <span>Generar PDF</span>
                    </button>
                </div>
            </section>

            <section class="ppv14-toolbar">
                <div class="ppv14-period-card">
                    <span class="ppv14-field-label">Periodo de programación</span>
                    <strong>${escapeHtml(state.period?.etiqueta || '')}</strong>
                    <small>${escapeHtml(periodDescription())}</small>
                </div>

                <div class="ppv14-turns">
                    <span class="ppv14-field-label">Turno</span>
                    <div class="ppv14-turn-list">${turnButtons()}</div>
                </div>

                <div class="ppv14-summary">
                    <div>
                        <strong>${state.rows.length}</strong>
                        <span>Puestos</span>
                    </div>
                    <div class="ok">
                        <strong>${assignedPrimary + extraCount}</strong>
                        <span>Personas</span>
                    </div>
                    <div class="${pending ? 'pending' : 'ok'}">
                        <strong>${pending}</strong>
                        <span>Sin principal</span>
                    </div>
                </div>

                ${renderMonthWeeks()}
            </section>

            <section class="ppv14-grid">
                ${state.rows.map(row => `
                    <article class="ppv14-machine-card ${row.esEspecial ? 'special' : ''}"
                             data-row-key="${escapeHtml(centerKey(row))}">
                        <div class="ppv14-machine">
                            <div class="ppv14-machine-icon">
                                <i class="fa-solid ${row.esEspecial ? 'fa-screwdriver-wrench' : 'fa-industry'}"></i>
                            </div>
                            <div>
                                <span>${row.esEspecial ? 'PUESTO ESPECIAL' : 'MÁQUINA'}</span>
                                <strong>${escapeHtml(row.esEspecial ? 'TORNILLO' : row.maquinaCodigo)}</strong>
                                <small>${escapeHtml(row.esEspecial ? 'Operación adicional' : row.maquinaNombre)}</small>
                            </div>
                        </div>

                        <div class="ppv14-piece-wrap">
                            <span class="ppv14-field-label">Pieza / referencia</span>
                            ${pieceControl(row)}
                        </div>

                        <div class="ppv14-operator-wrap">
                            <span class="ppv14-field-label">Operadores</span>
                            ${operatorArea(row)}
                        </div>
                    </article>
                `).join('')}
            </section>

            <div class="ppv14-footnote">
                <i class="fa-solid fa-circle-info"></i>
                <span>
                    El primer operador es el que Producción propondrá por defecto.
                    Los operadores agregados con <strong>+</strong> quedan como apoyo adicional.
                    Los cruces se muestran como advertencia amarilla y no bloquean la asignación.
                </span>
            </div>`;

        bindBoard();
        syncLegacyDetailFromBoard();
    }

    function syncLegacyDetailFromBoard() {
        if (!legacyTable || !state.period)
            return;

        const from = state.period.edicionDesde || state.period.filtroDesde;
        const to = state.period.edicionHasta || state.period.filtroHasta || from;

        state.rows
            .filter(row => !row.esEspecial && row.maquinaID)
            .forEach(row => {
                const selector =
                    `tr.ppv3-program-row[data-machine-id="${row.maquinaID}"][data-turn-id="${state.turnoId}"]`;

                legacyTable.querySelectorAll(selector).forEach(tr => {
                    const date = tr.dataset.plannerDate || '';

                    if (date && (date < from || date > to))
                        return;

                    const button = tr.querySelector('[data-assign]');
                    if (!button)
                        return;

                    // No falseamos historia ni una ejecución real en curso.
                    if (button.dataset.finished === '1' ||
                        button.dataset.producing === '1')
                        return;

                    const operatorId = row.operadorID ? String(row.operadorID) : '';
                    const operatorName = row.operadorNombre || '';

                    button.dataset.current = operatorId;
                    button.dataset.name = operatorName || 'Sin asignar';
                    tr.dataset.personId = operatorId;

                    button.classList.toggle('pending', !operatorId);
                    button.classList.remove(
                        'ppv7-operator-rojo',
                        'ppv7-operator-verde'
                    );
                    button.classList.add(
                        operatorId
                            ? 'ppv7-operator-verde'
                            : 'ppv7-operator-rojo'
                    );

                    const name = button.querySelector('.ppv3-assign-name');
                    if (name) {
                        name.innerHTML =
                            `<i class="ppv7-operator-dot"></i> ${escapeHtml(operatorName || 'Asignar operador')}`;
                    }

                    const meta = button.querySelector('.ppv3-assign-meta');
                    if (meta) {
                        meta.textContent = operatorId
                            ? 'Programado desde Distribución de Personal'
                            : 'Falta operador · clic para asignar';
                    }

                    const ofCell = tr.querySelector('td:nth-child(2)');
                    if (ofCell && operatorId) {
                        ofCell.querySelector('.ppv7-inherited-badge')?.remove();

                        if (!ofCell.querySelector('.ppv7-turn-badge') &&
                            !ofCell.querySelector('.ppv7-producing-badge') &&
                            !ofCell.querySelector('.ppv931-finished-badge')) {
                            const badge = document.createElement('span');
                            badge.className = 'ppv3-mini-badge ppv7-turn-badge';
                            badge.textContent = 'Distribución';
                            ofCell.appendChild(badge);
                        }
                    }
                });
            });
    }

    function findRow(key) {
        return state.rows.find(row => centerKey(row) === key);
    }

    async function loadBoard(turnId = state.turnoId) {
        closeActivePicker();
        setLoading();

        try {
            const data = await fetchJson(`${api.board}?${queryForBoard(turnId).toString()}`);

            state.period = data.periodo;
            state.turns = data.turnos || [];
            state.rows = data.filas || [];
            state.turnoId = Number(data.turnoSeleccionadoId || turnId || state.turns[0]?.turnoID || 0);

            if (state.period?.vista === 'mes') {
                const selected = state.period.semanasMes?.find(x => x.seleccionada);
                if (selected) {
                    state.semanaDesde = selected.desde;
                    const current = new URL(window.location.href);
                    current.searchParams.set('semanaDesde', selected.desde);
                    history.replaceState({}, '', current);
                }
            }

            render();
        } catch (error) {
            root.innerHTML = `
                <div class="alert alert-danger rounded-4 m-0">
                    <strong>No fue posible cargar la distribución.</strong>
                    <div class="mt-1">${escapeHtml(error.message)}</div>
                </div>`;
        }
    }

    async function loadCandidates(row) {
        const cacheKey = row.parteID ? `part:${row.parteID}` : 'all';

        if (state.candidatesCache.has(cacheKey))
            return state.candidatesCache.get(cacheKey);

        const data = await fetchJson(
            `${api.candidates}?parteId=${encodeURIComponent(row.parteID || '')}`
        );

        const candidates = data.operadores || [];
        state.candidatesCache.set(cacheKey, candidates);
        return candidates;
    }

    function closeActivePicker() {
        document.querySelectorAll('.ppv14-picker-popover').forEach(x => x.remove());
        state.activePicker = null;
    }

    function candidateText(candidate) {
        const level = Number(candidate.nivel || 0) > 0 ? ` · N${candidate.nivel}` : '';
        const control = candidate.numeroControl ? `${candidate.numeroControl} · ` : '';
        return `${control}${candidate.nombre}${level}`;
    }

    async function warningForSelection(row, operatorId) {
        const q = queryForBoard(state.turnoId);
        q.set('operadorId', String(operatorId));

        if (row.maquinaID)
            q.set('maquinaId', String(row.maquinaID));
        else
            q.set('centroEspecial', row.centroClave);

        const data = await fetchJson(`${api.warning}?${q.toString()}`);
        return data.warning || '';
    }

    async function openInlinePicker(button, row, mode) {
        closeActivePicker();

        const candidates = await loadCandidates(row);

        const popover = document.createElement('div');
        popover.className = 'ppv14-picker-popover ppv14-picker-portal';
        popover.innerHTML = `
            <div class="ppv14-picker-search">
                <i class="fa-solid fa-magnifying-glass"></i>
                <input type="search"
                       placeholder="Buscar por nombre o número de control..."
                       autocomplete="off" />
            </div>
            <div class="ppv14-picker-warning d-none"></div>
            <div class="ppv14-picker-results"></div>`;

        // V14.3: el selector vive directamente en <body>.
        // Así la última fila (incluido TORNILLO) nunca queda recortada por
        // overflow/z-index de cards, tablas o contenedores del planner.
        document.body.appendChild(popover);

        const input = popover.querySelector('input');
        const results = popover.querySelector('.ppv14-picker-results');
        const warningBox = popover.querySelector('.ppv14-picker-warning');

        const positionPopover = () => {
            const rect = button.getBoundingClientRect();
            const viewportPadding = 12;
            const gap = 6;
            const preferredWidth = Math.max(rect.width, 390);
            const width = Math.min(
                preferredWidth,
                window.innerWidth - viewportPadding * 2
            );

            const left = Math.min(
                Math.max(viewportPadding, rect.left),
                window.innerWidth - width - viewportPadding
            );

            const spaceBelow = window.innerHeight - rect.bottom - viewportPadding;
            const spaceAbove = rect.top - viewportPadding;
            const openUp = spaceBelow < 270 && spaceAbove > spaceBelow;
            const available = Math.max(
                190,
                (openUp ? spaceAbove : spaceBelow) - gap
            );

            popover.style.position = 'fixed';
            popover.style.left = `${Math.round(left)}px`;
            popover.style.width = `${Math.round(width)}px`;
            popover.style.maxHeight = `${Math.round(Math.min(410, available))}px`;
            popover.style.zIndex = '2147483000';

            results.style.maxHeight = `${Math.round(Math.max(120, Math.min(300, available - 105)))}px`;

            if (openUp) {
                popover.style.top = 'auto';
                popover.style.bottom = `${Math.round(window.innerHeight - rect.top + gap)}px`;
                popover.classList.add('opens-up');
            } else {
                popover.style.bottom = 'auto';
                popover.style.top = `${Math.round(rect.bottom + gap)}px`;
                popover.classList.remove('opens-up');
            }
        };

        const renderResults = query => {
            const q = normalize(query);

            const filtered = candidates
                .filter(candidate =>
                    !q ||
                    normalize(candidate.nombre).includes(q) ||
                    normalize(candidate.numeroControl).includes(q))
                .slice(0, 50);

            results.innerHTML = filtered.length
                ? filtered.map(candidate => `
                    <button type="button"
                            class="ppv14-picker-result"
                            data-candidate-id="${candidate.personaID}">
                        <span>
                            <strong>${escapeHtml(candidate.nombre)}</strong>
                            <small>
                                ${candidate.numeroControl ? `#${escapeHtml(candidate.numeroControl)}` : 'Sin número de control'}
                                ${candidate.nivel ? ` · Nivel ${candidate.nivel}` : ''}
                            </small>
                        </span>
                        <i class="fa-solid fa-chevron-right"></i>
                    </button>`).join('')
                : `<div class="ppv14-picker-empty">No hay coincidencias.</div>`;

            results.querySelectorAll('[data-candidate-id]').forEach(option => {
                option.addEventListener('click', async () => {
                    const operatorId = Number(option.dataset.candidateId);
                    const candidate = candidates.find(x => Number(x.personaID) === operatorId);

                    option.disabled = true;
                    warningBox.classList.add('d-none');
                    warningBox.innerHTML = '';

                    try {
                        const warning = await warningForSelection(row, operatorId);

                        if (warning) {
                            warningBox.classList.remove('d-none');
                            warningBox.innerHTML = `
                                <div>
                                    <i class="fa-solid fa-triangle-exclamation"></i>
                                    <span>${escapeHtml(warning)}</span>
                                </div>
                                <div class="ppv14-warning-actions">
                                    <button type="button" class="btn btn-sm btn-outline-secondary" data-warning-cancel>Cancelar</button>
                                    <button type="button" class="btn btn-sm btn-warning fw-bold" data-warning-continue>
                                        Asignar de todos modos
                                    </button>
                                </div>`;

                            warningBox.querySelector('[data-warning-cancel]')
                                ?.addEventListener('click', () => {
                                    warningBox.classList.add('d-none');
                                    option.disabled = false;
                                    positionPopover();
                                });

                            warningBox.querySelector('[data-warning-continue]')
                                ?.addEventListener('click', async () => {
                                    await assignFromPicker(row, mode, candidate);
                                });

                            positionPopover();
                            return;
                        }

                        await assignFromPicker(row, mode, candidate);
                    } catch (error) {
                        alert(error.message);
                        option.disabled = false;
                    }
                });
            });

            positionPopover();
        };

        input.addEventListener('input', () => renderResults(input.value));
        renderResults('');
        positionPopover();
        input.focus();

        state.activePicker = popover;
    }

    async function assignFromPicker(row, mode, candidate) {
        if (mode === 'extra') {
            const result = await saveExtra(row, {
                operatorNewId: Number(candidate.personaID)
            });

            if (result.warning)
                sessionStorage.setItem('ppv14-warning', result.warning);
        } else {
            const result = await savePrimary(row, {
                operatorId: Number(candidate.personaID)
            });

            if (result.warning)
                sessionStorage.setItem('ppv14-warning', result.warning);
        }

        closeActivePicker();
        await loadBoard(state.turnoId);
    }

    async function savePrimary(row, {
        operatorId = row.operadorID,
        partId = row.parteID,
        programId = row.programaProduccionID,
        reason = '',
        justification = ''
    } = {}) {
        const body = new FormData();
        body.append('__RequestVerificationToken', antiForgery);
        bodyPeriod(body);
        body.append('turnoId', String(state.turnoId));

        if (row.maquinaID)
            body.append('maquinaId', String(row.maquinaID));
        else
            body.append('centroEspecial', row.centroClave);

        if (programId) body.append('programaProduccionId', String(programId));
        if (partId) body.append('parteId', String(partId));
        if (operatorId) body.append('operadorId', String(operatorId));

        body.append('motivo', reason || '');
        body.append('justificacion', justification || '');

        return fetchJson(api.save, {
            method: 'POST',
            body,
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });
    }

    async function saveExtra(row, {
        operatorOldId = null,
        operatorNewId = null,
        remove = false,
        reason = '',
        justification = ''
    } = {}) {
        const body = new FormData();
        body.append('__RequestVerificationToken', antiForgery);
        bodyPeriod(body);
        body.append('turnoId', String(state.turnoId));

        if (row.maquinaID)
            body.append('maquinaId', String(row.maquinaID));
        else
            body.append('centroEspecial', row.centroClave);

        if (row.parteID)
            body.append('parteId', String(row.parteID));

        if (operatorOldId)
            body.append('operadorAnteriorId', String(operatorOldId));

        if (operatorNewId)
            body.append('operadorNuevoId', String(operatorNewId));

        body.append('eliminar', remove ? 'true' : 'false');
        body.append('motivo', reason || '');
        body.append('justificacion', justification || '');

        return fetchJson(api.extra, {
            method: 'POST',
            body,
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });
    }

    function reasonOptions() {
        return `
            <option value="">Seleccionar motivo...</option>
            <option value="AUSENCIA_O_RETIRO">Ausencia / retiro del operador</option>
            <option value="INCIDENCIA_O_EMERGENCIA">Incidencia / emergencia</option>
            <option value="CAMBIO_OPERATIVO">Cambio operativo de Producción</option>
            <option value="APOYO_OTRA_MAQUINA">Apoyo / reasignación a otra máquina</option>
            <option value="COMIDA_O_DESCANSO">Comida / descanso</option>
            <option value="FIN_TURNO_ANTICIPADO">Fin de turno / entrega anticipada</option>
            <option value="INDICACION_SUPERVISION">Indicación de supervisión</option>
            <option value="OTRO_OPERATIVO">Otro motivo operativo</option>`;
    }

    function ensureModals() {
        if (!document.getElementById('ppv14ChangeModal')) {
            document.body.insertAdjacentHTML('beforeend', `
<div class="modal fade" id="ppv14ChangeModal" tabindex="-1" aria-hidden="true">
  <div class="modal-dialog modal-dialog-centered">
    <div class="modal-content ppv14-modal-content">
      <div class="modal-header ppv14-modal-header">
        <div>
          <div class="ppv14-eyebrow">CAMBIO DE OPERADOR</div>
          <h5 class="modal-title" id="ppv14ChangeTitle">Cambiar operador</h5>
        </div>
        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
      </div>
      <div class="modal-body">
        <div id="ppv14ChangeWarning" class="ppv14-modal-warning d-none"></div>

        <label class="form-label fw-bold">Nuevo operador</label>
        <div class="ppv14-modal-search">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search"
                 id="ppv14ChangeSearch"
                 class="form-control"
                 placeholder="Buscar por nombre o número de control..."
                 autocomplete="off" />
        </div>
        <div id="ppv14ChangeCandidates" class="ppv14-modal-candidates"></div>

        <div class="row g-3 mt-1">
          <div class="col-md-5">
            <label class="form-label fw-bold">Motivo</label>
            <select id="ppv14ChangeReason" class="form-select">
              ${reasonOptions()}
            </select>
          </div>
          <div class="col-md-7">
            <label class="form-label fw-bold">Justificación</label>
            <textarea id="ppv14ChangeJustification"
                      class="form-control"
                      rows="3"
                      maxlength="500"
                      placeholder="Explica brevemente por qué se realiza el cambio."></textarea>
          </div>
        </div>

        <div id="ppv14ChangeError" class="alert alert-danger d-none mt-3 mb-0"></div>
      </div>
      <div class="modal-footer border-0">
        <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancelar</button>
        <button type="button" class="btn btn-outline-danger me-auto" id="ppv14RemoveOperator">
          <i class="fa-solid fa-user-minus"></i> Retirar
        </button>
        <button type="button" class="btn btn-primary fw-bold" id="ppv14SaveChange">
          <i class="fa-solid fa-check"></i> Guardar cambio
        </button>
      </div>
    </div>
  </div>
</div>`);
        }

        if (!document.getElementById('ppv14PieceModal')) {
            document.body.insertAdjacentHTML('beforeend', `
<div class="modal fade" id="ppv14PieceModal" tabindex="-1" aria-hidden="true">
  <div class="modal-dialog modal-dialog-centered modal-lg">
    <div class="modal-content ppv14-modal-content">
      <div class="modal-header ppv14-modal-header">
        <div>
          <div class="ppv14-eyebrow">PIEZA / REFERENCIA</div>
          <h5 class="modal-title" id="ppv14PieceTitle">Seleccionar pieza</h5>
        </div>
        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
      </div>
      <div class="modal-body">
        <div class="ppv14-piece-search-main">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search"
                 id="ppv14PartSearch"
                 class="form-control"
                 placeholder="Buscar número de parte, SAP o designación..."
                 autocomplete="off" />
        </div>

        <div id="ppv14ProgramSection" class="mt-3"></div>

        <div class="ppv14-section-title mt-3">Catálogo de piezas</div>
        <div id="ppv14PartResults" class="ppv14-part-results">
          <div class="ppv14-empty-option">Escribe al menos 2 caracteres para buscar.</div>
        </div>
      </div>
    </div>
  </div>
</div>`);
        }

        if (!document.getElementById('ppv14PdfSelectorModal')) {
            document.body.insertAdjacentHTML('beforeend', `
<div class="modal fade" id="ppv14PdfSelectorModal" tabindex="-1" aria-hidden="true">
  <div class="modal-dialog modal-dialog-centered modal-lg">
    <div class="modal-content ppv14-modal-content">
      <div class="modal-header ppv14-modal-header">
        <div>
          <div class="ppv14-eyebrow">DISTRIBUCIÓN DE PERSONAL</div>
          <h5 class="modal-title">Preparar distribución de personal</h5>
          <div class="text-muted small mt-1">Selecciona el rango y los turnos que aparecerán en el documento.</div>
        </div>
        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
      </div>
      <div class="modal-body">
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label fw-bold">Fecha desde</label>
            <input type="date" id="ppv14PdfFrom" class="form-control" />
          </div>
          <div class="col-md-6">
            <label class="form-label fw-bold">Fecha hasta</label>
            <input type="date" id="ppv14PdfTo" class="form-control" />
          </div>
        </div>

        <div class="d-flex justify-content-between align-items-center mt-4">
          <label class="form-label fw-bold mb-0">Turnos incluidos</label>
          <button type="button" class="btn btn-sm btn-link text-decoration-none" id="ppv14PdfAllTurns">Seleccionar todos</button>
        </div>
        <div id="ppv14PdfTurns" class="ppv14-pdf-turns"></div>

        <div id="ppv14PdfSelectorError" class="alert alert-danger d-none mt-3 mb-0"></div>
      </div>
      <div class="modal-footer border-0">
        <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancelar</button>
        <button type="button" class="btn btn-primary fw-bold" id="ppv14GeneratePdf">
          <i class="fa-solid fa-file-pdf"></i> Generar y previsualizar
        </button>
      </div>
    </div>
  </div>
</div>

<div class="modal fade" id="ppv14PdfViewerModal" tabindex="-1" aria-hidden="true">
  <div class="modal-dialog modal-xl modal-dialog-centered modal-dialog-scrollable">
    <div class="modal-content border-0 rounded-4 shadow-lg overflow-hidden">
      <div class="modal-header ppv14-pdf-viewer-header">
        <div>
          <div class="small text-uppercase fw-bold opacity-75">PRODUCCIÓN / DISTRIBUCIÓN DE PERSONAL</div>
          <h5 class="modal-title fw-bold mb-0">
            <i class="fa-solid fa-file-pdf me-2"></i>
            DISTRIBUCIÓN DE PERSONAL
          </h5>
        </div>
        <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
      </div>
      <div class="modal-body p-0 position-relative ppv14-pdf-viewer-body">
        <div id="ppv14PdfLoading" class="ppv14-pdf-loading">
          <div class="text-center p-4">
            <div class="spinner-border text-primary mb-3" role="status"></div>
            <div class="fw-bold">Generando distribución...</div>
            <div class="small text-muted mt-1">El PDF aparecerá aquí mismo.</div>
          </div>
        </div>
        <div id="ppv14PdfViewerError" class="alert alert-danger rounded-0 border-0 d-none mb-0"></div>
        <iframe id="ppv14PdfFrame"
                title="Distribución de operadores"
                src="about:blank"></iframe>
      </div>
      <div class="modal-footer border-0">
        <span id="ppv14PdfStatus" class="small text-muted me-auto">Preparando PDF...</span>
        <a id="ppv14PdfDownload"
           class="btn btn-primary disabled"
           href="#"
           aria-disabled="true">
          <i class="fa-solid fa-download me-1"></i>
          Descargar PDF
        </a>
        <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cerrar</button>
      </div>
    </div>
  </div>
</div>`);
        }
    }

    async function openChangeModal(row, extra = null) {
        ensureModals();

        const candidates = await loadCandidates(row);

        state.change = {
            row,
            extra,
            selectedId: extra ? Number(extra.operadorID) : Number(row.operadorID)
        };

        const currentName = extra ? extra.nombre : row.operadorNombre;
        document.getElementById('ppv14ChangeTitle').textContent =
            `${row.esEspecial ? 'TORNILLO' : row.maquinaCodigo} · ${currentName}`;

        document.getElementById('ppv14ChangeSearch').value = '';
        document.getElementById('ppv14ChangeReason').value = '';
        document.getElementById('ppv14ChangeJustification').value = '';
        document.getElementById('ppv14ChangeWarning').classList.add('d-none');
        document.getElementById('ppv14ChangeWarning').innerHTML = '';
        document.getElementById('ppv14ChangeError').classList.add('d-none');
        document.getElementById('ppv14ChangeError').textContent = '';

        const removeButton = document.getElementById('ppv14RemoveOperator');
        removeButton.innerHTML = extra
            ? '<i class="fa-solid fa-user-minus"></i> Retirar adicional'
            : '<i class="fa-solid fa-user-minus"></i> Dejar sin operador';

        const renderCandidates = filter => {
            const q = normalize(filter);

            const filtered = candidates
                .filter(candidate =>
                    !q ||
                    normalize(candidate.nombre).includes(q) ||
                    normalize(candidate.numeroControl).includes(q))
                .slice(0, 60);

            const host = document.getElementById('ppv14ChangeCandidates');

            host.innerHTML = filtered.map(candidate => `
                <button type="button"
                        class="ppv14-modal-candidate ${Number(candidate.personaID) === Number(state.change.selectedId) ? 'selected' : ''}"
                        data-change-candidate="${candidate.personaID}">
                    <span>
                        <strong>${escapeHtml(candidate.nombre)}</strong>
                        <small>
                            ${candidate.numeroControl ? `#${escapeHtml(candidate.numeroControl)}` : 'Sin número de control'}
                            ${candidate.nivel ? ` · N${candidate.nivel}` : ''}
                        </small>
                    </span>
                    <i class="fa-solid ${Number(candidate.personaID) === Number(state.change.selectedId) ? 'fa-circle-check' : 'fa-chevron-right'}"></i>
                </button>`).join('') || '<div class="ppv14-picker-empty">Sin coincidencias.</div>';

            host.querySelectorAll('[data-change-candidate]').forEach(button => {
                button.addEventListener('click', async () => {
                    state.change.selectedId = Number(button.dataset.changeCandidate);
                    renderCandidates(document.getElementById('ppv14ChangeSearch').value);

                    try {
                        const warning = await warningForSelection(row, state.change.selectedId);
                        const warningHost = document.getElementById('ppv14ChangeWarning');

                        if (warning) {
                            warningHost.classList.remove('d-none');
                            warningHost.innerHTML = `
                                <i class="fa-solid fa-triangle-exclamation"></i>
                                <span>${escapeHtml(warning)} <strong>Se permite continuar.</strong></span>`;
                        } else {
                            warningHost.classList.add('d-none');
                            warningHost.innerHTML = '';
                        }
                    } catch (error) {
                        document.getElementById('ppv14ChangeError').textContent = error.message;
                        document.getElementById('ppv14ChangeError').classList.remove('d-none');
                    }
                });
            });
        };

        document.getElementById('ppv14ChangeSearch').oninput = event => renderCandidates(event.target.value);
        renderCandidates('');

        bootstrap.Modal
            .getOrCreateInstance(document.getElementById('ppv14ChangeModal'))
            .show();

        setTimeout(() => document.getElementById('ppv14ChangeSearch')?.focus(), 250);
    }

    async function commitChange(remove = false) {
        if (!state.change) return;

        const { row, extra } = state.change;
        const reason = document.getElementById('ppv14ChangeReason').value;
        const justification = document.getElementById('ppv14ChangeJustification').value.trim();
        const errorHost = document.getElementById('ppv14ChangeError');

        errorHost.classList.add('d-none');
        errorHost.textContent = '';

        if (!reason || justification.length < 5) {
            errorHost.textContent = 'Selecciona un motivo y escribe una justificación de al menos 5 caracteres.';
            errorHost.classList.remove('d-none');
            return;
        }

        if (!remove && !state.change.selectedId) {
            errorHost.textContent = 'Selecciona el nuevo operador.';
            errorHost.classList.remove('d-none');
            return;
        }

        try {
            let result;

            if (extra) {
                result = await saveExtra(row, {
                    operatorOldId: Number(extra.operadorID),
                    operatorNewId: remove ? null : Number(state.change.selectedId),
                    remove,
                    reason,
                    justification
                });
            } else {
                result = await savePrimary(row, {
                    operatorId: remove ? null : Number(state.change.selectedId),
                    reason,
                    justification
                });
            }

            if (result.warning)
                sessionStorage.setItem('ppv14-warning', result.warning);

            bootstrap.Modal
                .getInstance(document.getElementById('ppv14ChangeModal'))
                ?.hide();

            await loadBoard(state.turnoId);
        } catch (error) {
            errorHost.textContent = error.message;
            errorHost.classList.remove('d-none');
        }
    }

    async function openPieceModal(row) {
        if (!row || row.esEspecial) return;

        ensureModals();
        state.pieceRow = row;

        document.getElementById('ppv14PieceTitle').textContent =
            `${row.maquinaCodigo} · seleccionar pieza`;

        const programHost = document.getElementById('ppv14ProgramSection');
        const resultHost = document.getElementById('ppv14PartResults');
        const search = document.getElementById('ppv14PartSearch');

        search.value = '';
        resultHost.innerHTML = '<div class="ppv14-empty-option">Escribe al menos 2 caracteres para buscar.</div>';
        programHost.innerHTML = '<div class="ppv14-loading-inline">Buscando producción prevista...</div>';

        try {
            const q = queryForBoard(state.turnoId);
            q.set('maquinaId', String(row.maquinaID));

            const data = await fetchJson(`${api.programs}?${q.toString()}`);
            const programs = data.programas || [];

            programHost.innerHTML = `
                <div class="ppv14-section-title">Producción prevista en el periodo</div>
                <div class="ppv14-program-list">
                    ${programs.length
                        ? programs.map(program => `
                            <button type="button"
                                    class="ppv14-program-option"
                                    data-program="${program.programaProduccionID}"
                                    data-part="${program.parteID || ''}">
                                <span>
                                    <strong>${escapeHtml(program.numeroParte || program.referenciaSAP || 'Sin número')}</strong>
                                    <small>${escapeHtml(program.descripcion || '')}</small>
                                    <em>${escapeHtml(program.of || 'Programa aún sin OF')}</em>
                                </span>
                                <i class="fa-solid fa-chevron-right"></i>
                            </button>`).join('')
                        : '<div class="ppv14-empty-option">Aún no hay producción ligada a esta máquina. Puedes seleccionar una pieza provisional del catálogo.</div>'}
                </div>`;

            programHost.querySelectorAll('[data-program]').forEach(button => {
                button.addEventListener('click', () => choosePiece(
                    Number(button.dataset.part || 0) || null,
                    Number(button.dataset.program || 0) || null
                ));
            });
        } catch (error) {
            programHost.innerHTML = `
                <div class="ppv14-empty-option warning">
                    No fue posible leer Producción en este momento. Puedes seleccionar una pieza provisional.
                </div>`;
        }

        bootstrap.Modal
            .getOrCreateInstance(document.getElementById('ppv14PieceModal'))
            .show();

        let timer = null;

        search.oninput = () => {
            clearTimeout(timer);
            timer = setTimeout(async () => {
                const term = search.value.trim();

                if (term.length < 2) {
                    resultHost.innerHTML = '<div class="ppv14-empty-option">Escribe al menos 2 caracteres para buscar.</div>';
                    return;
                }

                resultHost.innerHTML = '<div class="ppv14-loading-inline">Buscando piezas...</div>';

                try {
                    const data = await fetchJson(`${api.parts}?q=${encodeURIComponent(term)}`);

                    resultHost.innerHTML = (data.partes || []).length
                        ? data.partes.map(part => `
                            <button type="button"
                                    class="ppv14-part-option"
                                    data-manual-part="${part.parteID}">
                                <span>
                                    <strong>${escapeHtml(part.numeroParte)}</strong>
                                    <small>${escapeHtml(part.referenciaSAP || '')}</small>
                                    <em>${escapeHtml(part.descripcion || '')}</em>
                                </span>
                                <i class="fa-solid fa-check"></i>
                            </button>`).join('')
                        : '<div class="ppv14-empty-option">Sin coincidencias.</div>';

                    resultHost.querySelectorAll('[data-manual-part]').forEach(button => {
                        button.addEventListener('click', () => choosePiece(
                            Number(button.dataset.manualPart),
                            null
                        ));
                    });
                } catch (error) {
                    resultHost.innerHTML = `<div class="alert alert-danger mb-0">${escapeHtml(error.message)}</div>`;
                }
            }, 220);
        };

        setTimeout(() => search.focus(), 250);
    }

    async function choosePiece(partId, programId) {
        if (!state.pieceRow) return;

        try {
            await savePrimary(state.pieceRow, {
                operatorId: state.pieceRow.operadorID,
                partId,
                programId
            });

            bootstrap.Modal
                .getInstance(document.getElementById('ppv14PieceModal'))
                ?.hide();

            state.candidatesCache.clear();
            await loadBoard(state.turnoId);
        } catch (error) {
            alert(error.message);
        }
    }

    function openPdfSelector() {
        ensureModals();

        document.getElementById('ppv14PdfFrom').value =
            state.period?.filtroDesde || state.fechaDesde;

        document.getElementById('ppv14PdfTo').value =
            state.period?.filtroHasta || state.fechaHasta || state.fechaDesde;

        document.getElementById('ppv14PdfTurns').innerHTML =
            state.turns.map(turn => `
                <label class="ppv14-pdf-turn">
                    <input type="checkbox"
                           value="${turn.turnoID}"
                           ${Number(turn.turnoID) === Number(state.turnoId) ? 'checked' : ''} />
                    <span>
                        <strong>${escapeHtml(turn.nombre)}</strong>
                        <small>${escapeHtml(turn.tipo)}</small>
                    </span>
                </label>`).join('');

        const error = document.getElementById('ppv14PdfSelectorError');
        error.classList.add('d-none');
        error.textContent = '';

        bootstrap.Modal
            .getOrCreateInstance(document.getElementById('ppv14PdfSelectorModal'))
            .show();
    }

    function releasePdfBlob() {
        if (state.pdfBlobUrl) {
            URL.revokeObjectURL(state.pdfBlobUrl);
            state.pdfBlobUrl = null;
        }
    }

    async function generatePdfPreview() {
        const from = document.getElementById('ppv14PdfFrom').value;
        const to = document.getElementById('ppv14PdfTo').value;
        const turns = Array.from(
            document.querySelectorAll('#ppv14PdfTurns input:checked')
        ).map(x => x.value);

        const selectorError = document.getElementById('ppv14PdfSelectorError');

        if (!from || !to) {
            selectorError.textContent = 'Selecciona Fecha desde y Fecha hasta.';
            selectorError.classList.remove('d-none');
            return;
        }

        if (!turns.length) {
            selectorError.textContent = 'Selecciona al menos un turno.';
            selectorError.classList.remove('d-none');
            return;
        }

        selectorError.classList.add('d-none');

        const selectorModal = bootstrap.Modal.getInstance(
            document.getElementById('ppv14PdfSelectorModal')
        );

        const viewerModal = bootstrap.Modal.getOrCreateInstance(
            document.getElementById('ppv14PdfViewerModal')
        );

        const frame = document.getElementById('ppv14PdfFrame');
        const loading = document.getElementById('ppv14PdfLoading');
        const errorBox = document.getElementById('ppv14PdfViewerError');
        const status = document.getElementById('ppv14PdfStatus');
        const download = document.getElementById('ppv14PdfDownload');

        selectorModal?.hide();
        releasePdfBlob();

        frame.src = 'about:blank';
        loading.classList.remove('d-none');
        errorBox.classList.add('d-none');
        errorBox.textContent = '';
        status.textContent = 'Generando PDF en el servidor...';
        download.classList.add('disabled');
        download.setAttribute('aria-disabled', 'true');
        download.removeAttribute('download');
        download.href = '#';

        viewerModal.show();

        try {
            const q = new URLSearchParams({
                fechaDesde: from,
                fechaHasta: to,
                turnos: turns.join(',')
            });

            const response = await fetch(`${api.pdf}?${q.toString()}`, {
                credentials: 'same-origin'
            });

            if (!response.ok) {
                const message = (await response.text())
                    .replace(/<[^>]+>/g, ' ')
                    .replace(/\s+/g, ' ')
                    .trim()
                    .slice(0, 600);

                throw new Error(message || `No fue posible generar el PDF (${response.status}).`);
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.toLowerCase().includes('application/pdf'))
                throw new Error('El servidor no devolvió un PDF válido.');

            const blob = await response.blob();
            state.pdfBlobUrl = URL.createObjectURL(blob);

            frame.src = state.pdfBlobUrl;
            loading.classList.add('d-none');
            status.textContent = 'PDF listo para revisar o descargar.';

            const formatDownloadDate = value => {
                const [year, month, day] = value.split('-');
                return `${day}-${month}-${year}`;
            };

            const name = from === to
                ? `DISTRIBUCIÓN DE PERSONAL - ${formatDownloadDate(from)}.pdf`
                : `DISTRIBUCIÓN DE PERSONAL - ${formatDownloadDate(from)} AL ${formatDownloadDate(to)}.pdf`;

            download.href = state.pdfBlobUrl;
            download.download = name;
            download.classList.remove('disabled');
            download.setAttribute('aria-disabled', 'false');
        } catch (error) {
            loading.classList.add('d-none');
            errorBox.textContent = error.message;
            errorBox.classList.remove('d-none');
            status.textContent = 'No fue posible generar el PDF.';
        }
    }

    function bindBoard() {
        root.querySelectorAll('[data-turn-id]').forEach(button => {
            button.addEventListener('click', () => {
                state.turnoId = Number(button.dataset.turnId);
                loadBoard(state.turnoId);
            });
        });

        root.querySelectorAll('[data-week-start]').forEach(button => {
            button.addEventListener('click', () => {
                state.semanaDesde = button.dataset.weekStart || '';
                const current = new URL(window.location.href);
                current.searchParams.set('semanaDesde', state.semanaDesde);
                history.replaceState({}, '', current);
                loadBoard(state.turnoId);
            });
        });

        root.querySelectorAll('[data-picker-open]').forEach(button => {
            button.addEventListener('click', async event => {
                event.stopPropagation();

                const row = findRow(button.dataset.key);
                if (!row) return;

                try {
                    await openInlinePicker(
                        button,
                        row,
                        button.dataset.pickerOpen
                    );
                } catch (error) {
                    alert(error.message);
                }
            });
        });

        root.querySelectorAll('[data-primary-edit]').forEach(button => {
            button.addEventListener('click', () => {
                const row = findRow(button.closest('[data-row-key]')?.dataset.rowKey);
                if (row) openChangeModal(row, null);
            });
        });

        root.querySelectorAll('[data-extra-edit]').forEach(button => {
            button.addEventListener('click', () => {
                const row = findRow(button.closest('[data-row-key]')?.dataset.rowKey);
                if (!row) return;

                const extraId = Number(button.dataset.extraEdit);
                const extra = (row.extras || []).find(x => Number(x.extraID) === extraId);

                if (extra) openChangeModal(row, extra);
            });
        });

        root.querySelectorAll('[data-piece-open]').forEach(button => {
            button.addEventListener('click', () => {
                const row = findRow(button.dataset.key);
                if (row) openPieceModal(row);
            });
        });

        root.querySelector('[data-dismiss-warning]')?.addEventListener('click', () => {
            document.getElementById('ppv14WarningBanner')?.remove();
        });

        document.getElementById('ppv14LegacyToggle')?.addEventListener('click', event => {
            const hidden = legacyTable?.classList.toggle('ppv14-legacy-hidden');
            legacyHead?.classList.toggle('ppv14-legacy-hidden', hidden);

            event.currentTarget.innerHTML = hidden
                ? '<i class="fa-solid fa-list"></i><span>Ver detalle por OF</span>'
                : '<i class="fa-solid fa-xmark"></i><span>Ocultar detalle por OF</span>';
        });

        document.getElementById('ppv14PdfOpen')?.addEventListener('click', openPdfSelector);
    }

    document.addEventListener('click', event => {
        if (!event.target.closest('.ppv14-picker-popover') &&
            !event.target.closest('[data-picker-open]')) {
            closeActivePicker();
        }
    });

    window.addEventListener('resize', closeActivePicker);

    window.addEventListener('scroll', event => {
        if (state.activePicker && !state.activePicker.contains(event.target))
            closeActivePicker();
    }, true);

    document.addEventListener('click', event => {
        if (event.target.closest('#ppv14SaveChange'))
            commitChange(false);

        if (event.target.closest('#ppv14RemoveOperator'))
            commitChange(true);

        if (event.target.closest('#ppv14PdfAllTurns')) {
            event.preventDefault();
            document
                .querySelectorAll('#ppv14PdfTurns input[type="checkbox"]')
                .forEach(input => input.checked = true);
        }

        if (event.target.closest('#ppv14GeneratePdf'))
            generatePdfPreview();
    });

    ensureModals();

    document.getElementById('ppv14PdfViewerModal')?.addEventListener(
        'hidden.bs.modal',
        () => {
            document.getElementById('ppv14PdfFrame').src = 'about:blank';
            releasePdfBlob();
        }
    );

    loadBoard(null);
})();
