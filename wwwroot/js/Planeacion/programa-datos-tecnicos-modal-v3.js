(() => {
    'use strict';

    const MARKER = 'NSQ_DT_MODAL_FIX_V3';

    function byId(id) {
        return document.getElementById(id);
    }

    function toNumberOrNull(id) {
        const el = byId(id);
        if (!el) return null;

        const raw = String(el.value ?? '').trim();
        if (!raw) return null;

        const value = Number(raw);
        return Number.isFinite(value) ? value : null;
    }

    function ensureHidden(id) {
        let el = byId(id);
        if (el) return el;

        el = document.createElement('input');
        el.type = 'hidden';
        el.id = id;

        const modalBody =
            document.querySelector('#modalDatosTecnicosRapidos .modal-body');

        (modalBody || document.body).appendChild(el);
        return el;
    }

    function createField(definition) {
        if (byId(definition.id)) {
            return byId(definition.id);
        }

        let host = byId('nsqDtCamposFaltantesV3');

        if (!host) {
            const modalBody =
                document.querySelector('#modalDatosTecnicosRapidos .modal-body');

            if (!modalBody) return null;

            const wrapper = document.createElement('div');
            wrapper.id = 'nsqDtCamposFaltantesV3';
            wrapper.className = 'row g-3 mt-1';

            const title = document.createElement('div');
            title.className = 'col-12';

            title.innerHTML =
                '<div class="alert alert-warning mb-0">' +
                '<strong>Datos técnicos faltantes.</strong> ' +
                'Estos campos se agregaron automáticamente porque la vista actual no los contenía.' +
                '</div>';

            wrapper.appendChild(title);
            modalBody.appendChild(wrapper);
            host = wrapper;
        }

        const column = document.createElement('div');
        column.className = definition.column || 'col-md-4';

        const label = document.createElement('label');
        label.className = 'form-label';
        label.htmlFor = definition.id;
        label.textContent = definition.label;

        let control;

        if (definition.type === 'select') {
            control = document.createElement('select');
            control.className = 'form-select';
        } else {
            control = document.createElement('input');
            control.className = 'form-control';
            control.type = definition.type || 'text';

            if (definition.step) {
                control.step = definition.step;
            }

            if (definition.min !== undefined) {
                control.min = String(definition.min);
            }
        }

        control.id = definition.id;

        if (definition.placeholder) {
            control.placeholder = definition.placeholder;
        }

        column.appendChild(label);
        column.appendChild(control);
        host.appendChild(column);

        return control;
    }

    function ensureRequiredControls() {
        const definitions = [
            {
                id: 'dtMaterialID',
                label: 'Material *',
                type: 'select'
            },
            {
                id: 'dtMaquinaPrincipalID',
                label: 'Máquina principal *',
                type: 'select'
            },
            {
                id: 'dtMaquinaSustitutaID',
                label: 'Máquina sustituta',
                type: 'select'
            },
            {
                id: 'dtMoldePrincipalID',
                label: 'Molde *',
                type: 'select'
            },
            {
                id: 'dtCavidades',
                label: 'Cavidades *',
                type: 'number',
                min: 1,
                step: '1'
            },
            {
                id: 'dtObjetivoHora',
                label: 'Objetivo por hora *',
                type: 'number',
                min: 1,
                step: '1'
            },
            {
                id: 'dtCiclo',
                label: 'Ciclo *',
                type: 'text'
            },
            {
                id: 'dtPesoBrutoPieza',
                label: 'Peso bruto por pieza *',
                type: 'number',
                min: 0,
                step: '0.0001'
            },
            {
                id: 'dtEmbalajeCodigo',
                label: 'Código de embalaje *',
                type: 'text'
            },
            {
                id: 'dtPiezasPorEmbalaje',
                label: 'Piezas por embalaje *',
                type: 'number',
                min: 0,
                step: '0.0001'
            }
        ];

        definitions.forEach(createField);
    }

    function setMessage(message, type = 'info') {
        let box = byId('dtMensaje');

        if (!box) {
            const modalBody =
                document.querySelector('#modalDatosTecnicosRapidos .modal-body');

            if (!modalBody) return;

            box = document.createElement('div');
            box.id = 'dtMensaje';
            modalBody.prepend(box);
        }

        box.className = `alert alert-${type}`;
        box.textContent = message || '';

        if (!message) {
            box.classList.add('d-none');
        } else {
            box.classList.remove('d-none');
        }
    }

    function setValue(id, value) {
        const el = byId(id);
        if (!el) return;

        el.value =
            value === null || value === undefined
                ? ''
                : String(value);
    }

    function loadSelect(select, items, selectedValue, placeholder) {
        if (!select) return;

        const selected =
            selectedValue === null || selectedValue === undefined
                ? ''
                : String(selectedValue);

        select.innerHTML = '';

        const empty = document.createElement('option');
        empty.value = '';
        empty.textContent = placeholder;
        select.appendChild(empty);

        (Array.isArray(items) ? items : []).forEach(item => {
            const option = document.createElement('option');

            option.value =
                item.id === null || item.id === undefined
                    ? ''
                    : String(item.id);

            const code = String(item.codigo ?? '').trim();
            const name = String(item.nombre ?? '').trim();

            option.textContent =
                code && name && code.toLowerCase() !== name.toLowerCase()
                    ? `${code} - ${name}`
                    : (code || name || `ID ${option.value}`);

            if (option.value === selected) {
                option.selected = true;
            }

            select.appendChild(option);
        });

        select.dispatchEvent(
            new Event('change', { bubbles: true }));
    }

    function showModal(modalEl) {
        if (
            window.bootstrap &&
            window.bootstrap.Modal
        ) {
            window.bootstrap.Modal
                .getOrCreateInstance(modalEl)
                .show();

            return;
        }

        modalEl.style.display = 'block';
        modalEl.classList.add('show');
        modalEl.removeAttribute('aria-hidden');
        modalEl.setAttribute('aria-modal', 'true');
        modalEl.setAttribute('role', 'dialog');
        document.body.classList.add('modal-open');
    }

    function hideModal(modalEl) {
        if (
            window.bootstrap &&
            window.bootstrap.Modal
        ) {
            window.bootstrap.Modal
                .getOrCreateInstance(modalEl)
                .hide();

            return;
        }

        modalEl.style.display = 'none';
        modalEl.classList.remove('show');
        modalEl.setAttribute('aria-hidden', 'true');
        modalEl.removeAttribute('aria-modal');
        document.body.classList.remove('modal-open');
    }

    function missingFields() {
        const fields = [];

        if (!toNumberOrNull('dtMaterialID'))
            fields.push('Material');

        if (!toNumberOrNull('dtMaquinaPrincipalID'))
            fields.push('Máquina principal');

        if (!toNumberOrNull('dtMoldePrincipalID'))
            fields.push('Molde');

        const cavities = toNumberOrNull('dtCavidades');
        if (!cavities || cavities <= 0)
            fields.push('Cavidades');

        const target = toNumberOrNull('dtObjetivoHora');
        if (!target || target <= 0)
            fields.push('Objetivo por hora');

        if (!String(byId('dtCiclo')?.value ?? '').trim())
            fields.push('Ciclo');

        const grossWeight = toNumberOrNull('dtPesoBrutoPieza');
        if (!grossWeight || grossWeight <= 0)
            fields.push('Peso bruto por pieza');

        if (!String(byId('dtEmbalajeCodigo')?.value ?? '').trim())
            fields.push('Código de embalaje');

        const piecesPerPack = toNumberOrNull('dtPiezasPorEmbalaje');
        if (!piecesPerPack || piecesPerPack <= 0)
            fields.push('Piezas por embalaje');

        return fields;
    }

    async function openTechnicalData(btn) {
        const modalEl = byId('modalDatosTecnicosRapidos');

        if (!modalEl) {
            window.alert(
                'No se encontró el modal de datos técnicos en PlaneacionPrograma/Index.');
            return;
        }

        ensureRequiredControls();

        const parteId =
            String(btn.dataset.parteId ?? '').trim();

        const releaseDetalleId =
            String(btn.dataset.releaseDetalleId ?? '').trim();

        if (!parteId) {
            window.alert(
                'El renglón no tiene ParteID y no puede editarse.');
            return;
        }

        ensureHidden('dtParteID').value = parteId;
        ensureHidden('dtReleaseDetalleID').value = releaseDetalleId;

        const title =
            byId('dtParteTitulo') ||
            document.querySelector(
                '#modalDatosTecnicosRapidos .modal-title');

        if (title) {
            title.textContent =
                String(btn.dataset.parte ?? `Parte ${parteId}`);
        }

        setMessage('Cargando datos técnicos actuales...', 'info');

        // Se muestra antes del fetch para que el click SIEMPRE tenga respuesta visual.
        showModal(modalEl);

        try {
            const response = await fetch(
                `/PlaneacionPrograma/DatosTecnicosRapidos?parteId=${encodeURIComponent(parteId)}`,
                {
                    method: 'GET',
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest',
                        'Accept': 'application/json'
                    },
                    cache: 'no-store'
                });

            const result = await response.json();

            if (!response.ok || !result || result.ok !== true) {
                throw new Error(
                    result?.mensaje ||
                    `No fue posible cargar los datos técnicos (${response.status}).`);
            }

            const data = result.data || {};

            loadSelect(
                byId('dtMaterialID'),
                result.materiales,
                data.materialID,
                '-- Material --');

            loadSelect(
                byId('dtMaquinaPrincipalID'),
                result.maquinas,
                data.maquinaPrincipalID,
                '-- Máquina principal --');

            loadSelect(
                byId('dtMaquinaSustitutaID'),
                result.maquinas,
                data.maquinaSustitutaID,
                '-- Sin máquina sustituta --');

            loadSelect(
                byId('dtMoldePrincipalID'),
                result.moldes,
                data.moldePrincipalID,
                '-- Molde --');

            setValue('dtColor', data.color);
            setValue('dtCavidades', data.cavidades);
            setValue('dtObjetivoHora', data.objetivoHora);
            setValue('dtPiezasPorCaja', data.piezasPorCaja);
            setValue('dtCiclo', data.ciclo);
            setValue('dtTipoSecado', data.tipoSecado);
            setValue('dtHorasSecado', data.horasSecado);
            setValue('dtPesoBrutoPieza', data.pesoBrutoPieza);
            setValue('dtPesoNetoPieza', data.pesoNetoPieza);
            setValue('dtEmbalajeCodigo', data.embalajeCodigo);
            setValue('dtEmbalajeDescripcion', data.embalajeDescripcion);
            setValue('dtPiezasPorEmbalaje', data.piezasPorEmbalaje);

            if (title) {
                const number =
                    String(data.numeroParte ?? btn.dataset.parte ?? '').trim();

                const description =
                    String(data.descripcion ?? '').trim();

                title.textContent =
                    [number, description]
                        .filter(Boolean)
                        .join(' · ');
            }

            const missing = missingFields();

            if (missing.length) {
                setMessage(
                    `Completa los datos obligatorios: ${missing.join(', ')}.`,
                    'warning');
            } else {
                setMessage(
                    'Los datos técnicos obligatorios están completos. Puedes guardar y continuar a Programar molde.',
                    'success');
            }
        }
        catch (error) {
            setMessage(
                error instanceof Error
                    ? error.message
                    : 'No fue posible cargar los datos técnicos.',
                'danger');
        }
    }

    function buildPayload() {
        return {
            parteID:
                Number(byId('dtParteID')?.value || 0),

            materialID:
                toNumberOrNull('dtMaterialID'),

            maquinaPrincipalID:
                toNumberOrNull('dtMaquinaPrincipalID'),

            maquinaSustitutaID:
                toNumberOrNull('dtMaquinaSustitutaID'),

            moldePrincipalID:
                toNumberOrNull('dtMoldePrincipalID'),

            color:
                String(byId('dtColor')?.value ?? '').trim() || null,

            cavidades:
                toNumberOrNull('dtCavidades'),

            objetivoHora:
                toNumberOrNull('dtObjetivoHora'),

            piezasPorCaja:
                toNumberOrNull('dtPiezasPorCaja'),

            ciclo:
                String(byId('dtCiclo')?.value ?? '').trim() || null,

            tipoSecado:
                String(byId('dtTipoSecado')?.value ?? '').trim() || null,

            horasSecado:
                toNumberOrNull('dtHorasSecado'),

            pesoBrutoPieza:
                toNumberOrNull('dtPesoBrutoPieza'),

            pesoNetoPieza:
                toNumberOrNull('dtPesoNetoPieza'),

            embalajeCodigo:
                String(byId('dtEmbalajeCodigo')?.value ?? '').trim() || null,

            embalajeDescripcion:
                String(byId('dtEmbalajeDescripcion')?.value ?? '').trim() || null,

            piezasPorEmbalaje:
                toNumberOrNull('dtPiezasPorEmbalaje')
        };
    }

    async function saveTechnicalData(button) {
        ensureRequiredControls();

        const missing = missingFields();

        if (missing.length) {
            setMessage(
                `No se puede continuar. Falta: ${missing.join(', ')}.`,
                'danger');

            return;
        }

        const parteId =
            Number(byId('dtParteID')?.value || 0);

        const releaseDetalleId =
            Number(byId('dtReleaseDetalleID')?.value || 0);

        if (!parteId) {
            setMessage('No se pudo identificar la parte.', 'danger');
            return;
        }

        const token =
            document.querySelector(
                '#dtTokenForm input[name="__RequestVerificationToken"]')?.value ||
            document.querySelector(
                'input[name="__RequestVerificationToken"]')?.value ||
            '';

        const previousHtml = button.innerHTML;

        button.disabled = true;
        button.innerHTML =
            '<i class="fa-solid fa-spinner fa-spin me-1"></i> Guardando...';

        setMessage(
            'Guardando datos técnicos...',
            'info');

        try {
            const response = await fetch(
                '/PlaneacionPrograma/GuardarDatosTecnicosRapidos',
                {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json',
                        'RequestVerificationToken': token,
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    body: JSON.stringify(buildPayload())
                });

            const result = await response.json();

            if (!response.ok || !result || result.ok !== true) {
                throw new Error(
                    result?.mensaje ||
                    `No fue posible guardar los datos técnicos (${response.status}).`);
            }

            setMessage(
                'Datos técnicos guardados. Abriendo Programar molde...',
                'success');

            const modalEl = byId('modalDatosTecnicosRapidos');

            window.setTimeout(() => {
                if (modalEl) {
                    hideModal(modalEl);
                }

                if (releaseDetalleId > 0) {
                    window.location.href =
                        `/PlaneacionPrograma/CrearDesdeNecesidad?releaseDetalleId=${encodeURIComponent(releaseDetalleId)}`;
                } else {
                    window.location.reload();
                }
            }, 300);
        }
        catch (error) {
            setMessage(
                error instanceof Error
                    ? error.message
                    : 'No fue posible guardar los datos técnicos.',
                'danger');

            button.disabled = false;
            button.innerHTML = previousHtml;
        }
    }

    function init() {
        if (window[MARKER]) return;
        window[MARKER] = true;

        const modalEl = byId('modalDatosTecnicosRapidos');
        const saveButton = byId('btnGuardarDtRapidos');

        if (!modalEl) {
            console.error(
                '[NSQ] No existe #modalDatosTecnicosRapidos.');
            return;
        }

        if (!saveButton) {
            console.error(
                '[NSQ] No existe #btnGuardarDtRapidos.');
            return;
        }

        ensureRequiredControls();

        // Capture + stopImmediatePropagation: este handler domina al
        // handler inline anterior, que actualmente está incompleto/inconsistente.
        document.addEventListener(
            'click',
            event => {
                const btn =
                    event.target.closest('.btn-datos-tecnicos');

                if (!btn) return;

                event.preventDefault();
                event.stopImmediatePropagation();

                openTechnicalData(btn);
            },
            true);

        saveButton.addEventListener(
            'click',
            event => {
                event.preventDefault();
                event.stopImmediatePropagation();

                saveTechnicalData(saveButton);
            },
            true);

        console.info(
            '[NSQ] Modal de datos técnicos V3 listo.');
    }

    if (document.readyState === 'complete') {
        init();
    } else {
        window.addEventListener(
            'load',
            init,
            { once: true });
    }
})();
