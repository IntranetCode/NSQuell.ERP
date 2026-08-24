/* Extraido de la funcionalidad comun del antiguo _AlmacenLayout.
   Ahora las vistas usan _Layout.cshtml y cargan comportamiento por archivo JS. */

(() => {
                document.querySelectorAll('.alm-grid').forEach(grid => {
                    if (grid.querySelector('.alm-kpi')) {
                        grid.hidden = true;
                        grid.setAttribute('aria-hidden', 'true');
                    }
                });
            })();

(() => {
            const normalize = value => (value || '')
                .toString()
                .toLocaleLowerCase('es-MX')
                .normalize('NFD')
                .replace(/[\u0300-\u036f]/g, '')
                .replace(/\s+/g, ' ')
                .trim();

            const initialize = root => {
                const scope = root && root.querySelectorAll ? root : document;

                scope.querySelectorAll('[data-alm-smart-search]').forEach(component => {
                    if (component.dataset.smartReady === '1') return;
                    component.dataset.smartReady = '1';

                    const input = component.querySelector('[data-alm-smart-input]');
                    const menu = component.querySelector('[data-alm-smart-menu]');
                    const options = Array.from(component.querySelectorAll('[data-alm-smart-option]'));
                    const empty = component.querySelector('[data-alm-smart-empty]');
                    const submitOnSelect = component.dataset.almSmartSubmit === 'true';
                    const maxVisible = Number.POSITIVE_INFINITY; // ALMACEN_SMART_SEARCH_SIN_LIMITE_V9_1

                    if (!input || !menu) return;

                    const close = () => { menu.hidden = true; };

                    const refresh = () => {
                        const query = normalize(input.value);
                        let visible = 0;

                        options.forEach(option => {
                            const text = normalize(option.dataset.almSmartText || option.textContent);
                            const match = !query || text.includes(query);
                            const show = match && visible < maxVisible;
                            option.hidden = !show;
                            if (show) visible++;
                        });

                        if (empty) empty.hidden = visible !== 0;
                        menu.hidden = visible === 0 && !empty;
                        if (empty && visible === 0) menu.hidden = false;
                    };

                    // ALMACEN_SMART_SEARCH_TARGET_SELECT_V9_2
                    const targetSelectId = component.dataset.almSmartSelectTarget || '';
                    const targetSelect = targetSelectId
                        ? document.getElementById(targetSelectId)
                        : null;

                    const displayForValue = value => {
                        const option = options.find(item =>
                            (item.dataset.almSmartValue || '') === (value || ''));
                        return option?.dataset.almSmartDisplay
                            || option?.dataset.almSmartValue
                            || '';
                    };

                    const syncTargetValidity = () => {
                        if (!targetSelect) return;
                        input.setCustomValidity(
                            targetSelect.value
                                ? ''
                                : 'Selecciona una opcion valida del listado.');
                    };

                    if (targetSelect && targetSelect.value && !input.value) {
                        input.value = displayForValue(targetSelect.value);
                    }
                    syncTargetValidity();

                    input.addEventListener('focus', refresh);
                    input.addEventListener('input', () => {
                        if (targetSelect) {
                            targetSelect.value = '';
                            targetSelect.dispatchEvent(new Event('change', { bubbles: true }));
                            syncTargetValidity();
                        }
                        refresh();
                    });

                    input.addEventListener('keydown', event => {
                        if (event.key === 'Escape') {
                            close();
                            return;
                        }

                        if (event.key === 'ArrowDown' && !menu.hidden) {
                            const first = options.find(option => !option.hidden);
                            if (first) {
                                event.preventDefault();
                                first.focus();
                            }
                        }
                    });

                    options.forEach(option => {
                        option.addEventListener('click', () => {
                            const value = option.dataset.almSmartValue || '';
                            const display = option.dataset.almSmartDisplay || value;

                            input.value = display;

                            if (targetSelect) {
                                targetSelect.value = value;
                                targetSelect.dispatchEvent(new Event('change', { bubbles: true }));
                                syncTargetValidity();
                            }
                            else {
                                input.dispatchEvent(new Event('change', { bubbles: true }));
                            }

                            close();

                            if (submitOnSelect) {
                                component.closest('form')?.requestSubmit();
                            }
                        });

                        option.addEventListener('keydown', event => {
                            if (event.key === 'Escape') {
                                input.focus();
                                close();
                            }
                        });
                    });

                    document.addEventListener('click', event => {
                        if (!component.contains(event.target)) close();
                    });
                });
            };

            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', () => initialize(document));
            }
            else {
                initialize(document);
            }
        })();

(() => {
    const adjustmentTypes = new Set(["AjustePositivo", "AjusteNegativo"]);

    function normalizeText(value) {
        return (value || "").replace(/\s+/g, " ").trim();
    }

    function applyTableLabels(root) {
        const scope = root && root.querySelectorAll ? root : document;

        scope.querySelectorAll(".alm-table").forEach(table => {
            const headers = Array.from(table.querySelectorAll("thead th"))
                .map(th => normalizeText(th.textContent));

            table.querySelectorAll("tbody tr").forEach(row => {
                Array.from(row.children).forEach((cell, index) => {
                    if (cell.tagName !== "TD" || cell.hasAttribute("colspan")) return;
                    if (!cell.dataset.label && headers[index]) {
                        cell.dataset.label = headers[index];
                    }
                });
            });
        });
    }

    function configureMovementForm(form) {
        const typeField = form.querySelector('[name="TipoMovimiento"]');
        const observations = form.querySelector('[name="Observaciones"]');

        if (!typeField || !observations) return;

        if (!observations.dataset.baseRequired) {
            observations.dataset.baseRequired = observations.required ? "true" : "false";
        }

        const label = observations.id
            ? form.querySelector(`label[for="${observations.id}"]`)
            : null;

        if (label && !label.dataset.originalText) {
            label.dataset.originalText = normalizeText(label.textContent) || "Observaciones";
        }

        let hint = form.querySelector("[data-adjustment-hint]");
        if (!hint) {
            hint = document.createElement("small");
            hint.dataset.adjustmentHint = "true";
            hint.className = "alm-filter-hint";
            hint.textContent = "En ajustes positivos y negativos debes indicar el motivo.";
            hint.hidden = true;
            observations.insertAdjacentElement("afterend", hint);
        }

        const refresh = () => {
            const isAdjustment = adjustmentTypes.has(typeField.value);
            const baseRequired = observations.dataset.baseRequired === "true";

            observations.required = baseRequired || isAdjustment;
            hint.hidden = !isAdjustment;

            if (label) {
                label.textContent = isAdjustment
                    ? "Motivo del ajuste *"
                    : label.dataset.originalText;
            }
        };

        typeField.addEventListener("change", refresh);
        refresh();
    }

    // NSQ_ALMACEN_MP_SPLIT_EMPTY_ZERO_V1
    // En entregas de MP a OF, Virgen y Molido son componentes opcionales:
    // uno puede quedar en cero. Un campo vacio se interpreta como 0.
    function configureSplitAmounts(form) {
        if (form.dataset.splitAmountsReady === "true") return;

        const cantidadV =
            form.querySelector('[name="CantidadVirgen"]');

        const cantidadM =
            form.querySelector('[name="CantidadMolido"]');

        if (!cantidadV && !cantidadM) return;

        form.dataset.splitAmountsReady = "true";

        const clearValidationMessage = input => {
            if (!input || !input.name) return;

            const message =
                form.querySelector(
                    `[data-valmsg-for="${input.name}"]`);

            if (!message) return;

            message.textContent = "";
            message.classList.remove(
                "field-validation-error");

            message.classList.add(
                "field-validation-valid");
        };

        const normalizeEmpty = (
            input,
            displayFourDecimals = false) => {

            if (!input) return;

            const raw =
                (input.value || "").trim();

            if (raw !== "") return;

            input.value =
                displayFourDecimals
                    ? "0.0000"
                    : "0";

            clearValidationMessage(input);

            input.dispatchEvent(
                new Event("input", {
                    bubbles: true
                }));

            input.dispatchEvent(
                new Event("change", {
                    bubbles: true
                }));
        };

        cantidadV?.addEventListener(
            "blur",
            () => normalizeEmpty(
                cantidadV,
                true));

        cantidadM?.addEventListener(
            "blur",
            () => normalizeEmpty(
                cantidadM,
                true));

        // Capturing = true para ejecutar ANTES de jQuery
        // Unobtrusive Validation y del bloqueo de submit.
        form.addEventListener(
            "submit",
            () => {
                normalizeEmpty(
                    cantidadV,
                    false);

                normalizeEmpty(
                    cantidadM,
                    false);
            },
            true);
    }
    // NSQ_ALMACEN_SUBMIT_UNLOCK_V1
    function configureSubmitLock(form) {
        if (form.dataset.submitLockReady === "true") return;
        form.dataset.submitLockReady = "true";

        const restoreSubmitButton = button => {
            button.disabled = false;
            button.removeAttribute("aria-disabled");

            if (button.tagName === "BUTTON") {
                if (button.dataset.originalHtml !== undefined) {
                    button.innerHTML =
                        button.dataset.originalHtml;

                    delete button.dataset.originalHtml;
                }
            }
            else if (
                button.dataset.originalValue !==
                undefined) {

                button.value =
                    button.dataset.originalValue;

                delete button.dataset.originalValue;
            }
        };

        form.addEventListener("submit", event => {
            if (
                event.defaultPrevented ||
                !form.checkValidity()) {
                return;
            }

            const buttons =
                Array.from(
                    form.querySelectorAll(
                        'button[type="submit"], input[type="submit"]'));

            buttons.forEach(button => {
                button.disabled = true;

                button.setAttribute(
                    "aria-disabled",
                    "true");

                if (button.tagName === "BUTTON") {
                    button.dataset.originalHtml =
                        button.innerHTML;

                    button.innerHTML =
                        '<i class="fa-solid fa-spinner fa-spin" aria-hidden="true"></i> Guardando...';
                }
                else {
                    button.dataset.originalValue =
                        button.value;

                    button.value = "Guardando...";
                }
            });

            // Puede existir un validador posterior (por ejemplo
            // jQuery Unobtrusive) que haga preventDefault().
            // En ese caso el navegador NO navega y el boton debe
            // volver a quedar disponible.
            window.setTimeout(() => {
                if (!event.defaultPrevented) return;

                buttons.forEach(
                    restoreSubmitButton);
            }, 0);
        });
    }

    function initialize(root) {
        applyTableLabels(root);

        const scope = root && root.querySelectorAll ? root : document;
        scope.querySelectorAll("form").forEach(form => {
            configureMovementForm(form);
            configureSplitAmounts(form);
            configureSubmitLock(form);
        });
    }

    document.addEventListener("DOMContentLoaded", () => initialize(document));

    const observer = new MutationObserver(mutations => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    initialize(node);
                }
            }
        }
    });

    observer.observe(document.documentElement, {
        childList: true,
        subtree: true
    });
})();

(() => {
            const normalizeVmLabel = value => {
                if (!value) return value;

                return value
                    .replace(/\bVIRGEN\b/gi, "V")
                    .replace(/\bMOLIDO\b/gi, "M");
            };

            const ignoredParents = new Set([
                "SCRIPT",
                "STYLE",
                "TEXTAREA",
                "NOSCRIPT"
            ]);

            const normalizeNode = root => {
                if (!root) return;

                if (root.nodeType === Node.TEXT_NODE) {
                    const parent = root.parentElement;
                    if (!parent || ignoredParents.has(parent.tagName)) return;

                    const updated = normalizeVmLabel(root.nodeValue);
                    if (updated !== root.nodeValue) {
                        root.nodeValue = updated;
                    }
                    return;
                }

                if (root.nodeType !== Node.ELEMENT_NODE &&
                    root.nodeType !== Node.DOCUMENT_NODE &&
                    root.nodeType !== Node.DOCUMENT_FRAGMENT_NODE) {
                    return;
                }

                if (root.nodeType === Node.ELEMENT_NODE) {
                    const element = root;

                    for (const attr of ["placeholder", "title", "aria-label"]) {
                        if (!element.hasAttribute(attr)) continue;

                        const current = element.getAttribute(attr);
                        const updated = normalizeVmLabel(current);

                        if (updated !== current) {
                            element.setAttribute(attr, updated);
                        }
                    }
                }

                const walker = document.createTreeWalker(
                    root,
                    NodeFilter.SHOW_TEXT
                );

                const nodes = [];
                while (walker.nextNode()) {
                    nodes.push(walker.currentNode);
                }

                for (const node of nodes) {
                    const parent = node.parentElement;
                    if (!parent || ignoredParents.has(parent.tagName)) continue;

                    const updated = normalizeVmLabel(node.nodeValue);
                    if (updated !== node.nodeValue) {
                        node.nodeValue = updated;
                    }
                }
            };

            const initialize = () => {
                normalizeNode(document.body);

                const observer = new MutationObserver(mutations => {
                    for (const mutation of mutations) {
                        for (const node of mutation.addedNodes) {
                            normalizeNode(node);
                        }

                        if (mutation.type === "characterData") {
                            normalizeNode(mutation.target);
                        }
                    }
                });

                observer.observe(document.body, {
                    childList: true,
                    subtree: true,
                    characterData: true
                });
            };

            if (document.readyState === "loading") {
                document.addEventListener("DOMContentLoaded", initialize, {
                    once: true
                });
            } else {
                initialize();
            }
        })();
