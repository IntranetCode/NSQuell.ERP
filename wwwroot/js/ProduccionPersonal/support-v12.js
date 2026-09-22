(() => {
    'use strict';

    const endpoint =
        '/ProduccionPersonal/GuardarCoberturaRolV12';

    let changeContext = null;

    function getHidden(button) {
        const cell =
            button.closest(
                '[data-role-cell]'
            );

        return (
            cell?.querySelector(
                '.ppv3-support-hidden'
            ) ||
            null
        );
    }

    function getToken(form) {
        return (
            form?.querySelector(
                'input[name="__RequestVerificationToken"]'
            ) ||
            document.querySelector(
                'input[name="__RequestVerificationToken"]'
            )
        )?.value || '';
    }

    function getScope(button,form) {
        const explicit =
            (
                button.dataset.supportScope ||
                ''
            )
            .trim()
            .toUpperCase();

        if (explicit === 'DIA' ||
            explicit === 'SEMANA') {
            return explicit;
        }

        return form?.querySelector(
            'input[name="FechaTrabajo"],input[name="fechaTrabajo"]'
        )
            ? 'DIA'
            : 'SEMANA';
    }

    function getDate(form,scope) {
        if (!form) {
            return '';
        }

        if (scope === 'DIA') {
            return (
                form.querySelector(
                    'input[name="FechaTrabajo"]'
                ) ||
                form.querySelector(
                    'input[name="fechaTrabajo"]'
                )
            )?.value || '';
        }

        return (
            form.querySelector(
                'input[name="SemanaInicio"]'
            ) ||
            form.querySelector(
                'input[name="semanaInicio"]'
            )
        )?.value || '';
    }

    function buildContext(button,hidden) {
        const form =
            hidden.closest('form');

        const scope =
            getScope(
                button,
                form
            );

        return {
            button,
            hidden,
            form,
            scope,
            date:
                getDate(
                    form,
                    scope
                ),
            turnId:
                button.dataset.turnoId || '',
            role:
                button.dataset.role || '',
            token:
                getToken(form)
        };
    }

    async function save(
        context,
        personaId,
        justification
    ) {
        if (!context.date) {
            throw new Error(
                'No fue posible determinar la fecha de cobertura.'
            );
        }

        const body =
            new FormData();

        body.append(
            '__RequestVerificationToken',
            context.token
        );

        body.append(
            'alcance',
            context.scope
        );

        body.append(
            'fechaClave',
            context.date
        );

        body.append(
            'turnoID',
            context.turnId
        );

        body.append(
            'rol',
            context.role
        );

        body.append(
            'personaID',
            personaId || ''
        );

        body.append(
            'justificacion',
            justification || ''
        );

        const response =
            await fetch(
                endpoint,
                {
                    method:'POST',
                    body,
                    credentials:'same-origin',
                    headers:{
                        'X-Requested-With':
                            'XMLHttpRequest'
                    }
                }
            );

        let data = null;

        try {
            data =
                await response.json();
        } catch {
            data = null;
        }

        if (!response.ok ||
            !data?.ok) {
            throw new Error(
                data?.message ||
                'No fue posible guardar la cobertura.'
            );
        }

        return data;
    }

    function rolePlaceholder(role) {
        if (role === 'TECNICO') {
            return 'Seleccionar tecnico...';
        }

        if (role === 'SMED') {
            return 'Seleccionar SMED...';
        }

        return 'Seleccionar auxiliar...';
    }

    function setBusy(element,busy) {
        if (!element) {
            return;
        }

        element.disabled =
            !!busy;

        element.classList.toggle(
            'ppv12-saving',
            !!busy
        );
    }

    function createInlineSelect(
        button,
        hidden
    ) {
        if (!hidden ||
            hidden.value ||
            hidden.disabled) {
            return;
        }

        const cell =
            button.closest(
                '[data-role-cell]'
            );

        if (!cell) {
            return;
        }

        const existing =
            cell.querySelector(
                '.ppv12-support-inline'
            );

        if (existing) {
            button.classList.add(
                'd-none'
            );

            return;
        }

        const select =
            document.createElement(
                'select'
            );

        select.className =
            'form-select form-select-sm ppv12-support-inline';

        select.innerHTML =
            hidden.innerHTML;

        select.value =
            '';

        if (select.options.length) {
            select.options[0].textContent =
                rolePlaceholder(
                    button.dataset.role || ''
                );
        }

        select.addEventListener(
            'change',
            async () => {
                const personaId =
                    select.value;

                if (!personaId) {
                    return;
                }

                const context =
                    buildContext(
                        button,
                        hidden
                    );

                setBusy(
                    select,
                    true
                );

                try {
                    await save(
                        context,
                        personaId,
                        ''
                    );

                    window.location.reload();
                } catch (error) {
                    select.value =
                        '';

                    window.alert(
                        error?.message ||
                        'No fue posible guardar.'
                    );
                } finally {
                    setBusy(
                        select,
                        false
                    );
                }
            }
        );

        hidden.insertAdjacentElement(
            'afterend',
            select
        );

        button.classList.add(
            'd-none'
        );
    }

    function ensureJustification() {
        if (
            document.getElementById(
                'ppv12SupportJustification'
            )
        ) {
            return;
        }

        const modal =
            document.getElementById(
                'ppv3SupportModal'
            );

        const body =
            modal?.querySelector(
                '.modal-body'
            );

        if (!modal ||
            !body) {
            return;
        }

        const group =
            document.createElement(
                'div'
            );

        group.className =
            'mt-3 ppv12-justification-group';

        group.innerHTML = `
            <label class="form-label fw-bold"
                   for="ppv12SupportJustification">
                Justificacion del cambio
                <span class="text-danger">*</span>
            </label>
            <textarea id="ppv12SupportJustification"
                      class="form-control"
                      maxlength="500"
                      rows="3"
                      placeholder="Indica por que se cambia la persona asignada."></textarea>
            <div class="form-text">
                Obligatoria para reemplazar o retirar una asignacion existente.
            </div>
        `;

        body.appendChild(group);
    }

    function hideLegacySaveButtons() {
        document
            .querySelectorAll(
                'button[type="submit"],input[type="submit"]'
            )
            .forEach(button => {
                const text =
                    (
                        button.textContent ||
                        button.value ||
                        ''
                    )
                    .replace(/\s+/g,' ')
                    .trim()
                    .toLowerCase();

                if (
                    text.includes(
                        'guardar cobertura'
                    )
                ) {
                    button.classList.add(
                        'd-none'
                    );
                }
            });
    }

    function prepareInline() {
        document
            .querySelectorAll(
                '[data-support-open]'
            )
            .forEach(button => {
                const hidden =
                    getHidden(button);

                if (!hidden) {
                    return;
                }

                if (!hidden.value) {
                    createInlineSelect(
                        button,
                        hidden
                    );
                }
            });
    }

    document.addEventListener(
        'click',
        event => {
            const button =
                event.target.closest(
                    '[data-support-open]'
                );

            if (!button) {
                return;
            }

            const hidden =
                getHidden(button);

            if (!hidden) {
                return;
            }

            if (!hidden.value) {
                event.preventDefault();
                event.stopImmediatePropagation();

                createInlineSelect(
                    button,
                    hidden
                );

                button
                    .closest('[data-role-cell]')
                    ?.querySelector(
                        '.ppv12-support-inline'
                    )
                    ?.focus();

                return;
            }

            changeContext =
                buildContext(
                    button,
                    hidden
                );

            ensureJustification();

            const justification =
                document.getElementById(
                    'ppv12SupportJustification'
                );

            if (justification) {
                justification.value =
                    '';
            }
        },
        true
    );

    document.addEventListener(
        'click',
        async event => {
            const apply =
                event.target.closest(
                    '#ppv3SupportApply'
                );

            if (!apply ||
                !changeContext) {
                return;
            }

            event.preventDefault();
            event.stopImmediatePropagation();

            const modalSelect =
                document.getElementById(
                    'ppv3SupportSelect'
                );

            const justification =
                document.getElementById(
                    'ppv12SupportJustification'
                );

            const newId =
                modalSelect?.value || '';

            const oldId =
                changeContext.hidden.value || '';

            if (newId === oldId) {
                const modal =
                    document.getElementById(
                        'ppv3SupportModal'
                    );

                if (window.bootstrap &&
                    modal) {
                    window.bootstrap
                        .Modal
                        .getInstance(modal)
                        ?.hide();
                }

                return;
            }

            const reason =
                justification
                    ?.value
                    ?.trim() || '';

            if (reason.length < 5) {
                window.alert(
                    'Escribe una justificacion de al menos 5 caracteres.'
                );

                justification?.focus();
                return;
            }

            setBusy(
                apply,
                true
            );

            try {
                await save(
                    changeContext,
                    newId,
                    reason
                );

                window.location.reload();
            } catch (error) {
                window.alert(
                    error?.message ||
                    'No fue posible guardar el cambio.'
                );
            } finally {
                setBusy(
                    apply,
                    false
                );
            }
        },
        true
    );

    document.addEventListener(
        'DOMContentLoaded',
        () => {
            ensureJustification();
            hideLegacySaveButtons();
            prepareInline();
        }
    );
})();