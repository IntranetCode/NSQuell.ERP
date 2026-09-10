(function () {
    if (window.__NSQ_CTQ_V15_LOADED__) return;
    window.__NSQ_CTQ_V15_LOADED__ = true;

    const ROOT_SELECTOR = '#ctq-workspace-root';
    const CACHE_TTL = 45000;
    const cache = new Map();
    let controller = null;

    function sameOrigin(url) {
        try { return new URL(url, window.location.href).origin === window.location.origin; }
        catch { return false; }
    }

    function ensureLoader() {
        let loader = document.getElementById('ctq15WorkspaceLoading');
        if (!loader) {
            loader = document.createElement('div');
            loader.id = 'ctq15WorkspaceLoading';
            loader.className = 'ctq15-workspace-loading';
            loader.hidden = true;
            loader.innerHTML = '<div class="ctq15-loader-card"><span class="spinner-border spinner-border-sm me-2"></span>Cargando CONTPAQ...</div>';
            document.body.appendChild(loader);
        }
        return loader;
    }

    function setLoading(value) {
        const loader = ensureLoader();
        loader.hidden = !value;
    }

    function currentFilterForm() {
        return document.querySelector(ROOT_SELECTOR + ' form[data-ctq-filter-form]');
    }

    function mergeCurrentFilters(targetUrl, preserveTargetDates) {
        const form = currentFilterForm();
        const url = new URL(targetUrl, window.location.href);
        if (!form) return url.toString();

        const data = new FormData(form);
        const names = ['desde','hasta','buscar','proveedor','rfc','estatus','moneda','centroCosto','almacen','tamanoPagina','rangoPersonalizado'];
        names.forEach(function (name) {
            if (preserveTargetDates && (name === 'desde' || name === 'hasta')) return;
            if (!data.has(name)) return;
            const value = String(data.get(name) || '').trim();
            if (value) url.searchParams.set(name, value);
            else url.searchParams.delete(name);
        });
        url.searchParams.delete('pagina');
        return url.toString();
    }

    function parseWorkspace(html) {
        const doc = new DOMParser().parseFromString(html, 'text/html');
        const root = doc.querySelector(ROOT_SELECTOR);
        if (!root) return null;
        return { root: root, title: doc.title || document.title };
    }

    async function fetchHtml(url, useCache) {
        const key = url;
        const cached = cache.get(key);
        const now = Date.now();
        if (useCache && cached && (now - cached.time) < CACHE_TTL) return cached.html;

        if (controller) controller.abort();
        controller = new AbortController();

        const response = await fetch(url, {
            method: 'GET',
            credentials: 'same-origin',
            signal: controller.signal,
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'X-CTQ-Workspace': '1'
            }
        });
        if (!response.ok) throw new Error('HTTP ' + response.status);
        const html = await response.text();
        cache.set(key, { html: html, time: now });
        return html;
    }

    async function loadWorkspace(url, pushState, useCache) {
        if (!sameOrigin(url)) {
            window.location.href = url;
            return;
        }

        setLoading(true);
        try {
            const html = await fetchHtml(url, useCache !== false);
            const parsed = parseWorkspace(html);
            const current = document.querySelector(ROOT_SELECTOR);
            if (!parsed || !current) {
                window.location.href = url;
                return;
            }

            current.replaceWith(parsed.root);
            document.title = parsed.title;
            window.scrollTo({ top: 0, behavior: 'instant' });

            if (pushState) history.pushState({ ctq: true }, '', url);
        }
        catch (err) {
            if (err && err.name === 'AbortError') return;
            console.error('CTQ workspace:', err);
            window.location.href = url;
        }
        finally {
            setLoading(false);
        }
    }

    function ensureModal() {
        let modal = document.getElementById('ctq15DetailModal');
        if (modal) return modal;

        modal = document.createElement('div');
        modal.className = 'modal fade';
        modal.id = 'ctq15DetailModal';
        modal.tabIndex = -1;
        modal.innerHTML = `
            <div class="modal-dialog modal-xl modal-dialog-scrollable">
                <div class="modal-content">
                    <div class="modal-header">
                        <div>
                            <div class="ctq15-kpi-label">Compras CONTPAQ</div>
                            <h5 class="modal-title mb-0">Detalle de compra</h5>
                        </div>
                        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Cerrar"></button>
                    </div>
                    <div class="modal-body"><div class="text-center py-5"><span class="spinner-border"></span></div></div>
                </div>
            </div>`;
        document.body.appendChild(modal);
        return modal;
    }

    async function openDetailModal(url) {
        const modalEl = ensureModal();
        const body = modalEl.querySelector('.modal-body');
        body.innerHTML = '<div class="text-center py-5"><span class="spinner-border"></span><div class="mt-2 fw-bold">Consultando detalle...</div></div>';
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();

        try {
            const html = await fetchHtml(url, true);
            const doc = new DOMParser().parseFromString(html, 'text/html');
            const detail = doc.querySelector('.ctqd-page');
            if (!detail) throw new Error('No se encontró el contenido del detalle.');

            const clone = detail.cloneNode(true);
            clone.querySelectorAll('.erp-page-breadcrumb,.ctq15-subnav,.ctq-subnav').forEach(function (x) { x.remove(); });
            clone.querySelectorAll('a').forEach(function (a) {
                const text = (a.textContent || '').toLowerCase();
                if (text.includes('volver al listado') || text.includes('compras contpaq')) a.remove();
            });
            body.innerHTML = '';
            body.appendChild(clone);
        }
        catch (err) {
            console.error('CTQ detalle:', err);
            body.innerHTML = '<div class="alert alert-warning">No fue posible cargar el detalle dentro del modal. <a href="' + url + '">Abrir como página</a>.</div>';
        }
    }

    document.addEventListener('click', function (event) {
        const anchor = event.target.closest('a');
        if (!anchor) return;

        const href = anchor.getAttribute('href');
        if (!href || href === '#' || anchor.target === '_blank') return;

        if (/\/Compras\/ContpaqiDetalle/i.test(href)) {
            event.preventDefault();
            openDetailModal(new URL(href, window.location.href).toString());
            return;
        }

        if (anchor.hasAttribute('data-ctq-ajax')) {
            event.preventDefault();
            const preserveTargetDates = !!anchor.closest('.ctq15-period-nav');
            const target = mergeCurrentFilters(new URL(href, window.location.href).toString(), preserveTargetDates);
            loadWorkspace(target, true, true);
        }
    });

    document.addEventListener('submit', function (event) {
        const form = event.target.closest('form[data-ctq-filter-form], form[data-ctq-ajax-form]');
        if (!form) return;
        event.preventDefault();

        const data = new FormData(form);
        const url = new URL(form.action || window.location.href, window.location.href);
        url.search = '';
        for (const pair of data.entries()) {
            const key = pair[0];
            const value = String(pair[1] || '').trim();
            if (value) url.searchParams.append(key, value);
        }
        url.searchParams.delete('pagina');
        cache.clear();
        loadWorkspace(url.toString(), true, false);
    });

    window.addEventListener('popstate', function () {
        loadWorkspace(window.location.href, false, true);
    });
})();
