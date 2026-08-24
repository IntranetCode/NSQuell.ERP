// NSQ_PLANEACION_SELECT_SEARCH_V3
(() => {
    'use strict';

    const pagePath = (window.location.pathname || '').toLowerCase();
    if (!pagePath.startsWith('/planeacion')) return;

    const MARK = 'nsqSearchSelectV3';

    const normalize = value => (value || '')
        .toString()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase()
        .trim();

    function hasRealSelection(select) {
        return !!(select && select.value && String(select.value).trim());
    }

    function selectedText(select) {
        if (!hasRealSelection(select)) return '';
        const option = select.options[select.selectedIndex];
        return option ? option.textContent.trim() : '';
    }

    function findReusableSearchInput(select) {
        // PlaneacionRelease/Crear ya traía un buscador separado para Parte.
        // Lo reutilizamos como el UNICO campo visible y desactivamos su
        // antiguo listener quitando la clase parte-busqueda.
        const previous = select.previousElementSibling;

        if (
            previous instanceof HTMLInputElement &&
            previous.type === 'search' &&
            previous.classList.contains('parte-busqueda')
        ) {
            previous.classList.remove('parte-busqueda');
            previous.classList.remove('form-control');
            previous.classList.remove('mb-2');
            previous.classList.add('nsq-search-select__input');
            return previous;
        }

        return null;
    }

    function enhance(select) {
        if (!(select instanceof HTMLSelectElement)) return;
        if (select.dataset[MARK] === '1') return;
        if (select.multiple) return;
        if (select.hasAttribute('data-no-search')) return;
        if (select.closest('.nsq-search-select')) return;

        select.dataset[MARK] = '1';

        const wrapper = document.createElement('div');
        wrapper.className = 'nsq-search-select';

        const parent = select.parentNode;
        const reusableInput = findReusableSearchInput(select);

        if (reusableInput) {
            // El input ya está en el DOM antes del select.
            parent.insertBefore(wrapper, reusableInput);
            wrapper.appendChild(reusableInput);
            wrapper.appendChild(select);
        } else {
            parent.insertBefore(wrapper, select);
            wrapper.appendChild(select);
        }

        select.classList.add('nsq-search-select__native');

        const control = document.createElement('div');
        control.className = 'nsq-search-select__control';

        const input = reusableInput || document.createElement('input');

        if (!reusableInput) {
            input.type = 'search';
            input.className = 'nsq-search-select__input';
            input.autocomplete = 'off';
            input.spellcheck = false;
        }

        input.placeholder =
            select.dataset.searchPlaceholder ||
            input.placeholder ||
            'Buscar o seleccionar...';

        // CRITICO: si el select sólo tiene el placeholder seleccionado
        // ("-- Selecciona cliente --", "Cargando...", etc.), NO se copia
        // ese texto al buscador. El campo inicia vacío y permite escribir
        // inmediatamente.
        input.value = selectedText(select);

        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'nsq-search-select__toggle';
        toggle.tabIndex = -1;
        toggle.setAttribute('aria-label', 'Mostrar opciones');
        toggle.innerHTML =
            '<i class="fa-solid fa-chevron-down" aria-hidden="true"></i>';

        const dropdown = document.createElement('div');
        dropdown.className = 'nsq-search-select__dropdown';
        dropdown.hidden = true;

        const empty = document.createElement('div');
        empty.className = 'nsq-search-select__empty';
        empty.textContent = 'Sin coincidencias';
        empty.hidden = true;

        // Si reutilizamos el input existente, ya está dentro del wrapper.
        // Lo movemos al control para que quede una sola línea visual.
        control.append(input, toggle);
        wrapper.insertBefore(control, select);
        wrapper.appendChild(dropdown);

        let activeIndex = -1;

        function close({ restore = true } = {}) {
            dropdown.hidden = true;
            wrapper.classList.remove('is-open');
            activeIndex = -1;

            if (restore) {
                input.value = selectedText(select);
            }
        }

        function choose(index) {
            const option = select.options[index];
            if (!option || option.disabled) return;

            select.selectedIndex = index;
            input.value = selectedText(select);

            select.dispatchEvent(
                new Event('input', { bubbles: true }));

            select.dispatchEvent(
                new Event('change', { bubbles: true }));

            close();
        }

        function render() {
            dropdown.innerHTML = '';

            [...select.options].forEach((option, index) => {
                if (option.hidden) return;

                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'nsq-search-select__option';
                button.dataset.index = String(index);
                button.dataset.search = normalize(option.textContent);
                button.textContent = option.textContent.trim();

                if (option.disabled) {
                    button.disabled = true;
                    button.classList.add('is-disabled');
                }

                if (option.selected && hasRealSelection(select)) {
                    button.classList.add('is-selected');
                }

                button.addEventListener(
                    'mousedown',
                    event => event.preventDefault());

                button.addEventListener(
                    'click',
                    () => choose(index));

                dropdown.appendChild(button);
            });

            dropdown.appendChild(empty);
            filter();
        }

        function filter() {
            const term = normalize(input.value);
            let count = 0;

            dropdown
                .querySelectorAll('.nsq-search-select__option')
                .forEach(button => {
                    // La opción placeholder se mantiene visible al abrir
                    // sin texto, pero NO participa cuando el usuario busca.
                    const option =
                        select.options[Number(button.dataset.index)];

                    const isPlaceholder =
                        !option ||
                        !String(option.value || '').trim();

                    const visible =
                        !term
                            ? true
                            : !isPlaceholder &&
                              (button.dataset.search || '').includes(term);

                    button.hidden = !visible;
                    button.classList.remove('is-active');

                    if (visible) count++;
                });

            empty.hidden = count !== 0;
            activeIndex = -1;
        }

        function open() {
            if (select.disabled) return;
            dropdown.hidden = false;
            wrapper.classList.add('is-open');
            filter();
        }

        function selectableButtons() {
            return [
                ...dropdown.querySelectorAll(
                    '.nsq-search-select__option:not(:disabled)')
            ].filter(button => !button.hidden);
        }

        function move(delta) {
            const buttons = selectableButtons();
            if (!buttons.length) return;

            activeIndex += delta;

            if (activeIndex < 0) {
                activeIndex = buttons.length - 1;
            }

            if (activeIndex >= buttons.length) {
                activeIndex = 0;
            }

            buttons.forEach(
                button =>
                    button.classList.remove('is-active'));

            buttons[activeIndex]
                .classList.add('is-active');

            buttons[activeIndex]
                .scrollIntoView({ block: 'nearest' });
        }

        function sync() {
            input.disabled = select.disabled;
            toggle.disabled = select.disabled;

            // No restaurar texto de placeholder.
            input.value = selectedText(select);

            dropdown
                .querySelectorAll('.nsq-search-select__option')
                .forEach(button => {
                    button.classList.toggle(
                        'is-selected',
                        hasRealSelection(select) &&
                        Number(button.dataset.index) ===
                            select.selectedIndex);
                });
        }

        input.addEventListener('focus', () => {
            // Si ya hay una selección real, seleccionar su texto para que
            // escribir lo reemplace en una sola tecla.
            if (hasRealSelection(select)) {
                input.select();
            }

            open();
        });

        input.addEventListener('input', () => {
            open();
            filter();
        });

        input.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                open();
                move(1);
                return;
            }

            if (event.key === 'ArrowUp') {
                event.preventDefault();
                open();
                move(-1);
                return;
            }

            if (event.key === 'Enter') {
                const buttons = selectableButtons();

                if (!dropdown.hidden &&
                    activeIndex >= 0 &&
                    buttons[activeIndex]) {
                    event.preventDefault();
                    buttons[activeIndex].click();
                }

                return;
            }

            if (event.key === 'Escape') {
                event.preventDefault();
                close();
                input.blur();
            }
        });

        toggle.addEventListener('click', () => {
            if (dropdown.hidden) {
                input.focus();
                open();
            } else {
                close();
            }
        });

        select.addEventListener('change', sync);

        document.addEventListener(
            'mousedown',
            event => {
                if (!wrapper.contains(event.target)) {
                    close();
                }
            });

        const selectObserver =
            new MutationObserver(() => {
                render();
                sync();
            });

        selectObserver.observe(select, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: [
                'disabled',
                'selected',
                'label'
            ]
        });

        render();
        sync();
    }

    function scan(root) {
        if (root instanceof HTMLSelectElement) {
            enhance(root);
        }

        if (!root || !root.querySelectorAll) return;

        root.querySelectorAll('select')
            .forEach(enhance);
    }

    document.addEventListener(
        'DOMContentLoaded',
        () => scan(document));

    const pageObserver =
        new MutationObserver(records => {
            records.forEach(record => {
                record.addedNodes.forEach(node => {
                    if (node.nodeType === Node.ELEMENT_NODE) {
                        scan(node);
                    }
                });
            });
        });

    pageObserver.observe(
        document.documentElement,
        {
            childList: true,
            subtree: true
        });
})();
