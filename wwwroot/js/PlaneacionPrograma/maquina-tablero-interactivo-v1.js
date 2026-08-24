(() => {
    'use strict';

    const MARKER = 'NSQ_MACHINE_BOARD_DRAG_V1';
    if (window[MARKER]) return;
    window[MARKER] = true;

    let draggedBlock = null;

    const pad = value => String(value).padStart(2, '0');

    function localIso(value) {
        return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}:00`;
    }

    function snapHour(date) {
        const ms = 60 * 60 * 1000;
        return new Date(Math.round(date.getTime() / ms) * ms);
    }

    function token() {
        return document.querySelector(
            '#machineBoardTokenForm input[name="__RequestVerificationToken"]'
        )?.value ||
        document.querySelector(
            'input[name="__RequestVerificationToken"]'
        )?.value || '';
    }

    function modalFor(element) {
        return element?.closest('[data-machine-board-modal]') || null;
    }

    function ensureStatus(modal) {
        let status = modal.querySelector('[data-machine-board-status]');
        if (status) return status;

        status = document.createElement('div');
        status.dataset.machineBoardStatus = '1';
        status.className = 'machine-board-status';
        status.textContent = 'Arrastra un bloque azul horizontalmente para cambiar día u hora.';

        const timeline = modal.querySelector('.timeline-wrap');
        timeline?.prepend(status);
        return status;
    }

    function setStatus(modal, message, type = '') {
        const box = ensureStatus(modal);
        box.className = `machine-board-status ${type}`.trim();
        box.textContent = message;
    }

    function getDropDate(track, event) {
        const start = new Date(track.dataset.inicioPeriodo || '');
        const end = new Date(track.dataset.finPeriodo || '');

        if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end <= start) {
            throw new Error('El tablero no tiene un rango de fechas válido.');
        }

        const rect = track.getBoundingClientRect();
        const x = Math.max(0, Math.min(event.clientX - rect.left, rect.width));
        const ratio = rect.width <= 0 ? 0 : x / rect.width;
        const raw = new Date(start.getTime() + ((end.getTime() - start.getTime()) * ratio));
        let result = snapHour(raw);

        if (result < start) result = start;

        const lastHour = new Date(end.getTime() - (60 * 60 * 1000));
        if (result > lastHour) result = lastHour;

        return result;
    }

    function ensureDropIndicator(track) {
        let indicator = track.querySelector('[data-machine-drop-indicator]');
        if (indicator) return indicator;

        indicator = document.createElement('div');
        indicator.dataset.machineDropIndicator = '1';
        indicator.className = 'machine-board-drop-indicator';
        indicator.innerHTML = '<span></span>';
        track.appendChild(indicator);
        return indicator;
    }

    function updateIndicator(track, event) {
        const date = getDropDate(track, event);
        const start = new Date(track.dataset.inicioPeriodo || '');
        const end = new Date(track.dataset.finPeriodo || '');
        const rect = track.getBoundingClientRect();
        const pct = ((date.getTime() - start.getTime()) / (end.getTime() - start.getTime())) * 100;
        const indicator = ensureDropIndicator(track);

        indicator.style.left = `${Math.max(0, Math.min(100, pct))}%`;
        indicator.querySelector('span').textContent =
            `${pad(date.getDate())}/${pad(date.getMonth() + 1)} ${pad(date.getHours())}:00`;
        indicator.classList.add('show');
    }

    function hideIndicators() {
        document.querySelectorAll('[data-machine-drop-indicator]')
            .forEach(x => x.classList.remove('show'));
    }

    async function postMove(payload) {
        const response = await fetch(
            '/PlaneacionCalendarioMaquinas/ReprogramarCalendario',
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    'RequestVerificationToken': token()
                },
                body: JSON.stringify(payload)
            }
        );

        const text = await response.text();
        let result = null;

        try {
            result = text ? JSON.parse(text) : null;
        } catch {
            throw new Error(`Respuesta inválida del servidor (${response.status}).`);
        }

        if (!response.ok) {
            throw new Error(result?.mensaje || `HTTP ${response.status}`);
        }

        return result;
    }

    async function moveBlock(block, track, event) {
        const modal = modalFor(track);
        if (!modal) return;

        const programaId = Number(block.dataset.programaId || 0);
        const machineId = Number(track.dataset.maquinaId || 0);

        if (!Number.isFinite(programaId) || programaId <= 0 ||
            !Number.isFinite(machineId) || machineId <= 0) {
            setStatus(modal, 'No se pudo identificar el programa o la máquina.', 'error');
            return;
        }

        let start;
        try {
            start = getDropDate(track, event);
        } catch (error) {
            setStatus(modal, error.message, 'error');
            return;
        }

        const payload = {
            programaProduccionID: programaId,
            maquinaID: machineId,
            inicio: localIso(start),
            duracionBloqueHoras: 0,
            redimensionado: false,
            forzarMaquina: false,
            confirmarMovimiento: false,
            trabajarDomingo: false
        };

        try {
            setStatus(
                modal,
                `Validando movimiento a ${pad(start.getDate())}/${pad(start.getMonth() + 1)} ${pad(start.getHours())}:00...`,
                'working'
            );

            let result = await postMove(payload);

            if (!result?.ok) {
                throw new Error(result?.mensaje || 'El movimiento no fue permitido.');
            }

            if (result.requiereConfirmacion) {
                const detail = [
                    result.resumen || result.mensaje || 'El calendario requiere confirmación.',
                    result.cambioTexto ? `Cambio: ${result.cambioTexto}` : '',
                    result.arranqueTexto ? `Arranque: ${result.arranqueTexto}` : '',
                    result.finTexto ? `Fin: ${result.finTexto}` : ''
                ].filter(Boolean).join('\n');

                if (!window.confirm(`${detail}\n\n¿Aplicar este movimiento?`)) {
                    setStatus(modal, 'Movimiento cancelado por el usuario.', '');
                    return;
                }

                result = await postMove({
                    ...payload,
                    confirmarMovimiento: true
                });

                if (!result?.ok) {
                    throw new Error(result?.mensaje || 'No se pudo aplicar el movimiento confirmado.');
                }
            }

            setStatus(modal, result.mensaje || 'Programa reprogramado correctamente.', 'success');

            try {
                sessionStorage.setItem('nsqMachineBoardOpen', String(machineId));
            } catch { }

            window.setTimeout(() => window.location.reload(), 500);
        } catch (error) {
            setStatus(
                modal,
                `No se pudo mover el programa: ${error instanceof Error ? error.message : error}`,
                'error'
            );
        }
    }

    document.addEventListener('dragstart', event => {
        const block = event.target.closest('.machine-board-block');
        if (!block) return;

        draggedBlock = block;
        block.classList.add('is-dragging');

        if (event.dataTransfer) {
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', block.dataset.programaId || '');
        }

        const modal = modalFor(block);
        if (modal) {
            setStatus(modal, 'Suelta el bloque sobre la hora y día deseados.', 'working');
        }
    });

    document.addEventListener('dragend', event => {
        const block = event.target.closest('.machine-board-block');
        block?.classList.remove('is-dragging');
        draggedBlock = null;
        hideIndicators();
    });

    document.addEventListener('dragover', event => {
        const track = event.target.closest('[data-machine-board-track]');
        if (!track || !draggedBlock) return;

        const sourceMachine = Number(draggedBlock.dataset.maquinaId || 0);
        const targetMachine = Number(track.dataset.maquinaId || 0);
        if (sourceMachine !== targetMachine) return;

        event.preventDefault();
        if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';

        try {
            updateIndicator(track, event);
        } catch { }
    });

    document.addEventListener('dragleave', event => {
        const track = event.target.closest('[data-machine-board-track]');
        if (!track) return;

        const related = event.relatedTarget;
        if (related && track.contains(related)) return;

        track.querySelector('[data-machine-drop-indicator]')?.classList.remove('show');
    });

    document.addEventListener('drop', event => {
        const track = event.target.closest('[data-machine-board-track]');
        if (!track || !draggedBlock) return;

        event.preventDefault();
        const block = draggedBlock;
        draggedBlock = null;
        hideIndicators();
        moveBlock(block, track, event);
    });

    function openRequestedBoard() {
        const params = new URLSearchParams(window.location.search);
        let machineId = params.get('abrirMaquinaId');

        if (!machineId) {
            try {
                machineId = sessionStorage.getItem('nsqMachineBoardOpen');
                sessionStorage.removeItem('nsqMachineBoardOpen');
            } catch { }
        }

        if (!machineId) return;

        const modalEl = document.querySelector(
            `[data-machine-board-modal][data-maquina-id="${CSS.escape(String(machineId))}"]`
        );

        if (!modalEl || !window.bootstrap?.Modal) return;

        window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
    }

    document.addEventListener('shown.bs.modal', event => {
        const modal = event.target.closest?.('[data-machine-board-modal]') ||
                      (event.target.matches?.('[data-machine-board-modal]') ? event.target : null);

        if (modal) {
            ensureStatus(modal);
        }
    });

    document.addEventListener('hidden.bs.modal', event => {
        if (!event.target.matches?.('[data-machine-board-modal]')) return;

        const params = new URLSearchParams(window.location.search);
        if (params.get('tableroEmbebido') !== '1') return;

        try {
            window.parent.postMessage(
                { type: 'NSQ_CLOSE_MACHINE_BOARD' },
                window.location.origin
            );
        } catch { }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', openRequestedBoard, { once: true });
    } else {
        openRequestedBoard();
    }
})();
