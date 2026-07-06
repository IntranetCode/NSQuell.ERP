document.addEventListener('DOMContentLoaded', function () {
    const panel = document.getElementById('slide-out-panel');
    const overlay = document.getElementById('panel-overlay');
    const panelContent = document.getElementById('panel-content');

    if (!panel || !overlay || !panelContent) {
        console.error('[usuarios-panel] Faltan elementos del panel.');
        return;
    }

    function showPanel() {
        panel.classList.add('is-open');
        overlay.classList.add('is-open');
    }

    function hidePanel() {
        panel.classList.remove('is-open');
        overlay.classList.remove('is-open');
    }

    function spinner() {
        return `
        <div class="d-flex justify-content-center align-items-center h-100 p-4">
            <div class="spinner-border" role="status" aria-label="Cargando"></div>
        </div>`;
    }

    function escapeHtml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function esRespuestaJson(contentType, texto) {
        const tipo = (contentType || '').toLowerCase();
        const body = (texto || '').trim();

        return tipo.includes('application/json') ||
            body.startsWith('{') ||
            body.startsWith('[');
    }

    function mostrarToastPanel(mensaje, tipo) {
        document.querySelectorAll('.toast-usuario-panel').forEach(x => x.remove());

        const esError = tipo === 'error';

        const alerta = document.createElement('div');
        alerta.className = 'toast-usuario-panel alert alert-dismissible fade show position-fixed shadow';
        alerta.classList.add(esError ? 'alert-danger' : 'alert-success');
        alerta.style.cssText = 'top:20px;right:20px;z-index:2147483000;max-width:620px;';

        alerta.innerHTML = `
            <div class="d-flex align-items-start gap-2">
                <i class="fas ${esError ? 'fa-exclamation-circle' : 'fa-check-circle'} mt-1"></i>
                <div>${escapeHtml(mensaje || (esError ? 'No fue posible guardar los cambios.' : 'Operación realizada correctamente.'))}</div>
                <button type="button" class="btn-close ms-2" aria-label="Cerrar"></button>
            </div>
        `;

        alerta.querySelector('.btn-close')?.addEventListener('click', function () {
            alerta.remove();
        });

        document.body.appendChild(alerta);

        setTimeout(function () {
            alerta.remove();
        }, esError ? 8000 : 5000);
    }

    function inicializarValidaciones() {
        if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
            panelContent.querySelectorAll('form').forEach(function (form) {
                window.jQuery.validator.unobtrusive.parse(form);
            });
        }
    }

    function inicializarSelect2Jefe() {
        let intentos = 0;

        const intervalo = setInterval(function () {
            intentos++;

            if (window.jQuery && typeof window.jQuery.fn.select2 !== 'undefined') {
                clearInterval(intervalo);

                const $sel = window.jQuery('#selectJefe');
                if ($sel.length) {
                    if ($sel.hasClass('select2-hidden-accessible')) {
                        $sel.select2('destroy');
                    }

                    $sel.select2({
                        theme: 'bootstrap-5',
                        width: '100%',
                        placeholder: '-- Escriba para buscar jefe... --',
                        minimumInputLength: 1,
                        allowClear: true,
                        dropdownParent: window.jQuery('#user-form').length
                            ? window.jQuery('#user-form')
                            : window.jQuery('body'),
                        ajax: {
                            url: '/Usuarios/BuscarPersonasJefes',
                            dataType: 'json',
                            delay: 300,
                            data: function (params) {
                                return { term: params.term };
                            },
                            processResults: function (data) {
                                return data;
                            },
                            error: function (xhr, status, err) {
                                console.error('❌ Select2 AJAX error:', status, err);
                            }
                        }
                    }).on('select2:open', function () {
                        setTimeout(function () {
                            document.querySelector('.select2-container--open .select2-search__field')?.focus();
                        }, 50);
                    });
                }

                const $depto = window.jQuery('#selectDepartamento');
                if ($depto.length) {
                    if ($depto.hasClass('select2-hidden-accessible')) {
                        $depto.select2('destroy');
                    }

                    $depto.select2({
                        theme: 'bootstrap-5',
                        width: '100%',
                        placeholder: '-- Seleccione un Departamento --',
                        allowClear: true,
                        dropdownParent: window.jQuery('#user-form').length
                            ? window.jQuery('#user-form')
                            : window.jQuery('body'),
                        language: {
                            noResults: function () {
                                return 'No se encontraron resultados';
                            }
                        }
                    });
                }

                console.log('✅ Select2 inicializado');
            }

            if (intentos >= 30) {
                clearInterval(intervalo);
                console.error('❌ Select2 no cargó en 3 segundos');
            }
        }, 100);
    }

    function inicializarPanelCargado() {
        inicializarValidaciones();
        inicializarSelect2Jefe();
    }

    async function openPanel(url) {
        if (!url) {
            console.error('[usuarios-panel] URL vacía');
            return;
        }

        panelContent.innerHTML = spinner();
        showPanel();

        try {
            const res = await fetch(url, {
                method: 'GET',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                },
                credentials: 'include'
            });

            const contentType = res.headers.get('content-type') || '';
            const texto = await res.text();

            if (!res.ok) {
                throw new Error(`Error del servidor: ${res.status}\n${texto.substring(0, 500)}`);
            }

            if (esRespuestaJson(contentType, texto)) {
                const data = JSON.parse(texto);

                if (!data.ok) {
                    throw new Error(data.message || 'No se pudo cargar el contenido.');
                }

                mostrarToastPanel(data.message || 'Operación realizada correctamente.', 'success');
                return;
            }

            panelContent.innerHTML = texto;
            inicializarPanelCargado();

        } catch (err) {
            console.error('Error al abrir el panel:', err);

            panelContent.innerHTML = `
            <div class="alert alert-danger m-3">
                No se pudo cargar el contenido.<br>
                <small>${escapeHtml((err && err.message) ? err.message : 'Error desconocido')}</small>
            </div>`;
        }
    }

    function closePanel() {
        hidePanel();

        setTimeout(function () {
            panelContent.innerHTML = '';
        }, 300);
    }

    async function handleFormSubmit(form, submitter) {
        const submitBtn = submitter && submitter.tagName === 'BUTTON'
            ? submitter
            : form.querySelector('button[type="submit"]');

        const originalHtml = submitBtn ? submitBtn.innerHTML : '';

        try {
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Guardando...';
            }

            const res = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'application/json, text/html;q=0.9, */*;q=0.8'
                },
                credentials: 'include'
            });

            if (res.redirected) {
                window.location.href = res.url;
                return;
            }

            const contentType = res.headers.get('content-type') || '';
            const texto = await res.text();

            if (esRespuestaJson(contentType, texto)) {
                let data;

                try {
                    data = JSON.parse(texto);
                } catch {
                    throw new Error('La respuesta del servidor no pudo interpretarse correctamente.');
                }

                if (!res.ok || !data.ok) {
                    throw new Error(data.message || 'No fue posible guardar los cambios.');
                }

                mostrarToastPanel(data.message || 'Usuario actualizado correctamente.', 'success');

                if (data.redirectUrl) {
                    setTimeout(function () {
                        window.location.href = data.redirectUrl;
                    }, 900);
                }

                return;
            }

            if (!res.ok) {
                throw new Error(`Error del servidor: ${res.status}\n${texto.substring(0, 500)}`);
            }

            panelContent.innerHTML = texto;
            inicializarPanelCargado();

        } catch (err) {
            console.error('Error al enviar el formulario:', err);

            mostrarToastPanel(
                (err && err.message) ? err.message : 'No se pudo enviar el formulario.',
                'error'
            );
        } finally {
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.innerHTML = originalHtml;
            }
        }
    }

    document.body.addEventListener('click', function (e) {
        const el = e.target.closest('#btn-crear-usuario, .btn-editar-usuario');

        if (!el) return;

        e.preventDefault();

        const url = el.getAttribute('href') ||
            el.getAttribute('data-url') ||
            (el.dataset ? el.dataset.url : null);

        openPanel(url);
    });

    panel.addEventListener('click', function (e) {
        if (e.target.matches('#close-panel-btn') || e.target.matches('#cancel-btn')) {
            e.preventDefault();
            closePanel();
        }
    });

    overlay.addEventListener('click', closePanel);

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            closePanel();
        }
    });

    panel.addEventListener('submit', function (e) {
        const form = e.target.closest('form');

        if (!form) return;

        if (e.defaultPrevented) return;

        const submitter = e.submitter || document.activeElement;

        e.preventDefault();
        e.stopPropagation();

        const isOverrides =
            form.id === 'ov-form' ||
            form.dataset.ajaxPermisos === 'true' ||
            (submitter && submitter.classList && submitter.classList.contains('save-overrides'));

        const isResetPassword = form.id === 'form-restablecer-password';

        if (!isOverrides && !isResetPassword && window.jQuery && window.jQuery.fn && window.jQuery.fn.valid) {
            if (!window.jQuery(form).valid()) return;
        }

        handleFormSubmit(form, submitter);
    });
});