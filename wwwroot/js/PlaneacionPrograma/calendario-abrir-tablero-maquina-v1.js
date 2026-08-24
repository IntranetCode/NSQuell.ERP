(() => {
    'use strict';

    const MARKER = 'NSQ_GENERAL_CAL_MACHINE_MODAL_V1';
    if (window[MARKER]) return;
    window[MARKER] = true;

    let modalElement = null;
    let iframe = null;

    function ensureModal() {
        if (modalElement) return modalElement;

        modalElement = document.createElement('div');
        modalElement.id = 'nsqModalTableroMaquinaGeneral';
        modalElement.className = 'modal fade';
        modalElement.tabIndex = -1;
        modalElement.setAttribute('aria-hidden', 'true');

        modalElement.innerHTML = `
            <div class="modal-dialog modal-fullscreen m-0">
                <div class="modal-content border-0 bg-dark position-relative">
                    <button type="button"
                            class="btn btn-light position-absolute top-0 end-0 m-3 shadow"
                            style="z-index:1085"
                            data-bs-dismiss="modal"
                            aria-label="Cerrar tablero">
                        <i class="fa-solid fa-xmark me-1"></i>Cerrar tablero
                    </button>
                    <div data-machine-board-loading
                         class="position-absolute top-50 start-50 translate-middle text-white text-center"
                         style="z-index:1080">
                        <div class="spinner-border mb-3" role="status"></div>
                        <div class="fw-bold">Cargando tablero de máquina...</div>
                    </div>
                    <iframe title="Tablero de máquina"
                            data-machine-board-frame
                            style="width:100%;height:100vh;border:0;background:#f4f7fb"></iframe>
                </div>
            </div>`;

        document.body.appendChild(modalElement);
        iframe = modalElement.querySelector('[data-machine-board-frame]');

        iframe.addEventListener('load', () => {
            modalElement.querySelector('[data-machine-board-loading]')?.classList.add('d-none');
        });

        modalElement.addEventListener('hidden.bs.modal', () => {
            if (iframe) iframe.src = 'about:blank';
            modalElement.querySelector('[data-machine-board-loading]')?.classList.remove('d-none');
        });

        return modalElement;
    }

    function openBoard(trigger) {
        const machineId = Number(trigger.dataset.maquinaId || 0);
        const from = trigger.dataset.fechaDesde || '';
        const to = trigger.dataset.fechaHasta || '';
        const baseUrl = trigger.dataset.boardUrl || '/PlaneacionPrograma/Maquinas';

        if (!Number.isFinite(machineId) || machineId <= 0) return;

        const modal = ensureModal();
        const query = new URLSearchParams();

        if (from) query.set('fechaDesde', from);
        if (to) query.set('fechaHasta', to);
        query.set('abrirMaquinaId', String(machineId));
        query.set('tableroEmbebido', '1');

        iframe.src = `${baseUrl}?${query.toString()}`;

        window.bootstrap?.Modal
            ?.getOrCreateInstance(modal)
            .show();
    }

    document.addEventListener('click', event => {
        const trigger = event.target.closest('[data-open-machine-board]');
        if (!trigger) return;

        event.preventDefault();
        event.stopPropagation();
        openBoard(trigger);
    });

    document.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;

        const trigger = event.target.closest('[data-open-machine-board]');
        if (!trigger) return;

        event.preventDefault();
        openBoard(trigger);
    });

    window.addEventListener('message', event => {
        if (event.origin !== window.location.origin) return;
        if (event.data?.type !== 'NSQ_CLOSE_MACHINE_BOARD') return;

        const modal = ensureModal();
        window.bootstrap?.Modal.getInstance(modal)?.hide();
    });
})();
