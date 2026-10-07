(() => {
    'use strict';

    function initPlanner(root) {
        if (!root || root.dataset.rhqReady === '1') return;
        root.dataset.rhqReady = '1';

        const escalaId = Number(root.dataset.escalaId || 0);
        const readonly = root.dataset.readonly === 'true';
        const body = root.querySelector('[data-rhq-body]');
        const search = root.querySelector('[data-rhq-search]');
        const selectVisible = root.querySelector('[data-rhq-select-visible]');
        const bulkTurn = root.querySelector('[data-rhq-bulk-turn]');
        const bulkMachine = root.querySelector('[data-rhq-bulk-machine]');
        const msg = root.querySelector('[data-rhq-message]');
        const totalEl = root.querySelector('[data-rhq-total]');
        const assignedEl = root.querySelector('[data-rhq-assigned]');
        const unassignedEl = root.querySelector('[data-rhq-unassigned]');
        const dirtyEl = root.querySelector('[data-rhq-dirty]');
        const token = root.querySelector('[data-rhq-token] input[name="__RequestVerificationToken"]')?.value || '';

        const state = { data: null, rows: new Map(), originals: new Map() };

        const norm = value => (value || '').toString().normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '').toUpperCase().trim();

        const esc = value => (value ?? '').toString()
            .replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;')
            .replaceAll('"','&quot;').replaceAll("'",'&#039;');

        function message(text, kind='neutral') {
            if (!msg) return;
            msg.textContent = text || '';
            msg.dataset.kind = kind;
        }

        function options(items, selected, placeholder, getText) {
            return `<option value="">${esc(placeholder)}</option>` +
                items.map(item => `<option value="${item.id}" ${Number(selected || 0) === Number(item.id) ? 'selected' : ''}>${esc(getText(item))}</option>`).join('');
        }

        function isDirty(row) {
            const original = state.originals.get(row.personalId);
            if (!original) return false;
            return Number(row.turnId || 0) !== Number(original.turnId || 0) ||
                   Number(row.machineId || 0) !== Number(original.machineId || 0);
        }

        function recalc() {
            const rows = [...state.rows.values()];
            const assigned = rows.filter(x => Number(x.turnId || 0) > 0).length;
            const dirty = rows.filter(isDirty).length;

            if (totalEl) totalEl.textContent = String(rows.length);
            if (assignedEl) assignedEl.textContent = String(assigned);
            if (unassignedEl) unassignedEl.textContent = String(rows.length - assigned);
            if (dirtyEl) dirtyEl.textContent = String(dirty);

            root.querySelectorAll('[data-rhq-row]').forEach(tr => {
                const id = Number(tr.dataset.personalId || 0);
                const row = state.rows.get(id);
                const badge = tr.querySelector('[data-rhq-status]');
                if (!row || !badge) return;

                tr.classList.toggle('is-dirty', isDirty(row));

                if (row.special) {
                    badge.className = 'rhq-status special';
                    badge.textContent = 'Ajuste especial';
                } else if (Number(row.turnId || 0) > 0) {
                    badge.className = 'rhq-status ok';
                    badge.textContent = isDirty(row) ? 'Cambio pendiente' : 'Programado';
                } else {
                    badge.className = 'rhq-status empty';
                    badge.textContent = isDirty(row) ? 'Quitar horario' : 'Sin horario';
                }
            });

            if (dirty === 0) message('Sin cambios pendientes.','neutral');
            else message(`${dirty} operador(es) con cambios sin guardar.`,'warning');
        }

        function selectedRows() {
            return [...root.querySelectorAll('[data-rhq-row]')]
                .filter(tr => !tr.hidden)
                .filter(tr => tr.querySelector('[data-rhq-check]')?.checked)
                .map(tr => state.rows.get(Number(tr.dataset.personalId || 0)))
                .filter(Boolean)
                .filter(row => !row.special);
        }

        function render() {
            const data = state.data;
            if (!data) return;

            bulkTurn.innerHTML = options(data.turnos || [], null,
                'Horario para seleccionados...', x => `${x.nombre} · ${x.horario}`);

            bulkMachine.innerHTML = `<option value="">Sin preferencia de máquina</option>` +
                (data.maquinas || []).map(x => `<option value="${x.id}">${esc(x.texto)}</option>`).join('');

            body.innerHTML = '';

            (data.operadores || []).forEach(op => {
                const row = {
                    personalId: Number(op.personalId),
                    control: op.numeroControl || '',
                    nombre: op.nombre || '',
                    piezas: Number(op.piezas || 0),
                    turnId: op.escalaTurnoId ? Number(op.escalaTurnoId) : null,
                    machineId: op.maquinaId ? Number(op.maquinaId) : null,
                    special: op.configuracionEspecial === true,
                    asignaciones: Number(op.asignacionesActivas || 0)
                };

                state.rows.set(row.personalId, row);
                state.originals.set(row.personalId, { turnId: row.turnId, machineId: row.machineId });

                const tr = document.createElement('tr');
                tr.dataset.rhqRow = '1';
                tr.dataset.personalId = String(row.personalId);
                tr.dataset.search = norm(`${row.control} ${row.nombre}`);
                if (row.special) tr.classList.add('is-special');

                const disabled = readonly || row.special;

                tr.innerHTML = `
                    <td class="rhq-check"><input type="checkbox" data-rhq-check ${disabled ? 'disabled' : ''} /></td>
                    <td><strong class="rhq-control">${esc(row.control || '—')}</strong><small>ID ${row.personalId}</small></td>
                    <td><strong>${esc(row.nombre)}</strong><small>${row.piezas} pieza(s) en Polivalencia</small></td>
                    <td>
                        <select data-rhq-turn ${disabled ? 'disabled' : ''}>
                            ${options(data.turnos || [], row.turnId, 'Sin horario / no programado', x => `${x.nombre} · ${x.horario}`)}
                        </select>
                        ${row.special ? '<small class="rhq-special-note">Tiene fechas o bloques especiales. Usa “Ajuste individual” si deseas conservarlos.</small>' : ''}
                    </td>
                    <td>
                        <select data-rhq-machine ${disabled ? 'disabled' : ''}>
                            ${options(data.maquinas || [], row.machineId, 'Sin preferencia de máquina', x => x.texto)}
                        </select>
                    </td>
                    <td><span class="rhq-status" data-rhq-status></span></td>`;

                const turn = tr.querySelector('[data-rhq-turn]');
                const machine = tr.querySelector('[data-rhq-machine]');

                turn?.addEventListener('change', () => {
                    row.turnId = Number(turn.value || 0) || null;
                    recalc();
                });

                machine?.addEventListener('change', () => {
                    row.machineId = Number(machine.value || 0) || null;
                    recalc();
                });

                body.appendChild(tr);
            });

            const prev = data.semanaAnterior || {};
            const copy = root.querySelector('[data-rhq-copy-previous]');
            if (copy) {
                copy.disabled = readonly || !prev.existe || !(prev.sugerencias || []).length;
                copy.title = prev.existe ? `Copiar asignaciones compatibles de ${prev.folio || 'la semana anterior'}` : 'No existe una escala anterior compatible.';
                copy.textContent = prev.existe ? `Usar semana anterior${prev.folio ? ` · ${prev.folio}` : ''}` : 'Sin semana anterior';
            }

            recalc();
            applySearch();
        }

        function applySearch() {
            const q = norm(search?.value || '');
            let visible = 0;

            root.querySelectorAll('[data-rhq-row]').forEach(tr => {
                const show = !q || (tr.dataset.search || '').includes(q);
                tr.hidden = !show;
                if (show) visible++;
            });

            if (selectVisible) {
                const checks = [...root.querySelectorAll('[data-rhq-row]')]
                    .filter(x => !x.hidden)
                    .map(x => x.querySelector('[data-rhq-check]'))
                    .filter(x => x && !x.disabled);
                selectVisible.checked = checks.length > 0 && checks.every(x => x.checked);
                selectVisible.indeterminate = checks.some(x => x.checked) && !selectVisible.checked;
            }

            if (q) message(`${visible} operador(es) coinciden con la búsqueda.`,'neutral');
        }

        function syncRowControls(row) {
            const tr = root.querySelector(`[data-rhq-row][data-personal-id="${row.personalId}"]`);
            if (!tr) return;
            const turn = tr.querySelector('[data-rhq-turn]');
            const machine = tr.querySelector('[data-rhq-machine]');
            if (turn) turn.value = row.turnId ? String(row.turnId) : '';
            if (machine) machine.value = row.machineId ? String(row.machineId) : '';
        }

        async function load() {
            message('Cargando operadores y horarios...','neutral');
            try {
                const response = await fetch(`/EscalaPersonal/OperadoresSemanaRapidaDatosV11?escalaId=${encodeURIComponent(escalaId)}`, {
                    credentials:'same-origin', headers:{'Accept':'application/json'}
                });
                const data = await response.json();
                if (!response.ok || !data?.ok) throw new Error(data?.mensaje || 'No fue posible cargar la programación rápida.');
                state.data = data;
                state.rows.clear();
                state.originals.clear();
                render();
            } catch (error) {
                body.innerHTML = `<tr><td colspan="6" class="rhq-error">${esc(error?.message || 'Error al cargar')}</td></tr>`;
                message(error?.message || 'No fue posible cargar la programación.','error');
            }
        }

        async function save() {
            if (readonly) return;
            const dirtyRows = [...state.rows.values()].filter(row => !row.special).filter(isDirty);
            if (!dirtyRows.length) {
                message('No hay cambios por guardar.','neutral');
                return;
            }

            const fd = new FormData();
            fd.append('__RequestVerificationToken',token);
            fd.append('EscalaID',String(escalaId));
            dirtyRows.forEach((row,index) => {
                fd.append(`Operadores[${index}].PersonalID`,String(row.personalId));
                fd.append(`Operadores[${index}].EscalaTurnoID`,row.turnId ? String(row.turnId) : '');
                fd.append(`Operadores[${index}].MaquinaID`,row.machineId ? String(row.machineId) : '');
            });

            root.querySelectorAll('[data-rhq-save],[data-rhq-save-bottom]').forEach(btn => btn.disabled = true);
            message(`Guardando ${dirtyRows.length} cambio(s)...`,'neutral');

            try {
                const response = await fetch('/EscalaPersonal/GuardarOperadoresSemanaRapidaV11', {
                    method:'POST', body:fd, credentials:'same-origin',
                    headers:{'X-Requested-With':'XMLHttpRequest','Accept':'application/json'}
                });
                const data = await response.json();
                if (!response.ok || !data?.ok) throw new Error(data?.mensaje || 'No fue posible guardar.');
                message(data.mensaje || 'Programación guardada.','success');
                await load();
                document.dispatchEvent(new CustomEvent('nsq:rrhh-escala-operadores-actualizada',{detail:{escalaId}}));
            } catch (error) {
                message(error?.message || 'No fue posible guardar.','error');
            } finally {
                root.querySelectorAll('[data-rhq-save],[data-rhq-save-bottom]').forEach(btn => btn.disabled = readonly);
            }
        }

        search?.addEventListener('input',applySearch);

        selectVisible?.addEventListener('change',() => {
            root.querySelectorAll('[data-rhq-row]').forEach(tr => {
                if (tr.hidden) return;
                const check = tr.querySelector('[data-rhq-check]');
                if (check && !check.disabled) check.checked = selectVisible.checked;
            });
        });

        root.querySelector('[data-rhq-apply-turn]')?.addEventListener('click',() => {
            const selected = selectedRows();
            if (!selected.length) { message('Selecciona uno o más operadores visibles.','warning'); return; }
            const value = Number(bulkTurn.value || 0) || null;
            selected.forEach(row => { row.turnId = value; syncRowControls(row); });
            recalc();
        });

        root.querySelector('[data-rhq-apply-machine]')?.addEventListener('click',() => {
            const selected = selectedRows();
            if (!selected.length) { message('Selecciona uno o más operadores visibles.','warning'); return; }
            const value = Number(bulkMachine.value || 0) || null;
            selected.forEach(row => { row.machineId = value; syncRowControls(row); });
            recalc();
        });

        root.querySelector('[data-rhq-copy-previous]')?.addEventListener('click',() => {
            const prev = state.data?.semanaAnterior;
            const suggestions = prev?.sugerencias || [];
            if (!suggestions.length) return;
            let applied = 0;
            suggestions.forEach(s => {
                const row = state.rows.get(Number(s.personalId));
                if (!row || row.special) return;
                row.turnId = Number(s.escalaTurnoId || 0) || null;
                row.machineId = Number(s.maquinaId || 0) || null;
                syncRowControls(row);
                applied++;
            });
            recalc();
            message(`${applied} operador(es) tomaron la configuración compatible de ${prev.folio || 'la semana anterior'}. Revisa y guarda.`,'warning');
        });

        root.querySelector('[data-rhq-reset]')?.addEventListener('click',() => {
            state.rows.forEach(row => {
                if (row.special) return;
                const original = state.originals.get(row.personalId);
                row.turnId = original?.turnId || null;
                row.machineId = original?.machineId || null;
                syncRowControls(row);
            });
            root.querySelectorAll('[data-rhq-check]').forEach(x => x.checked = false);
            if (selectVisible) { selectVisible.checked = false; selectVisible.indeterminate = false; }
            recalc();
        });

        root.querySelector('[data-rhq-save]')?.addEventListener('click',save);
        root.querySelector('[data-rhq-save-bottom]')?.addEventListener('click',save);
        load();
    }

    function init() {
        document.querySelectorAll('[data-rhq-planner]').forEach(initPlanner);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded',init,{once:true});
    else init();
})();