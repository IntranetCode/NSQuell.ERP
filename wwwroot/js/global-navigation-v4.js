/* NSQ_GLOBAL_NAV_V4_JS */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    if (!root || root.dataset.v4Ready === '1') return;

    root.dataset.v4Ready = '1';
    document.body?.classList.add('nsq-unified-navbar-active');

    const activeDepartment = (root.dataset.activeDepartment || '').trim();
    const brandTitle = document.querySelector(
        '.top-navbar .nsq-brand-title, .top-navbar .navbar-title');

    if (brandTitle) {
        brandTitle.textContent = activeDepartment || 'NS QUELL - ERP';
        brandTitle.setAttribute('title', brandTitle.textContent);
    }

    const strip = root.querySelector('[data-nsq-context-strip]');
    const scrollLeftButton = root.querySelector('[data-nsq-scroll-left]');
    const scrollRightButton = root.querySelector('[data-nsq-scroll-right]');

    const menuButtons = Array.from(
        root.querySelectorAll('[data-nsq-menu-button]'));
    const menuPanels = Array.from(
        root.querySelectorAll('[data-nsq-menu-panel]'));

    const departmentsButton =
        root.querySelector('[data-nsq-departments-button]');
    const departmentsPanel =
        root.querySelector('[data-nsq-departments-panel]');

    const mobileToggle = root.querySelector('[data-nsq-mobile-toggle]');
    const mobilePanel = root.querySelector('[data-nsq-mobile-panel]');

    const desktopPanels = [
        ...menuPanels,
        ...(departmentsPanel ? [departmentsPanel] : [])
    ];

    let closeTimer = null;
    let openButton = null;
    let openPanel = null;

    const isMobile = () =>
        window.matchMedia('(max-width: 960px)').matches;

    const clearCloseTimer = () => {
        if (!closeTimer) return;
        window.clearTimeout(closeTimer);
        closeTimer = null;
    };

    const placePanel = (trigger, panel) => {
        if (!trigger || !panel || !panel.classList.contains('is-open')) {
            return;
        }

        const safe = 12;
        const gap = 6;

        panel.style.visibility = 'hidden';
        panel.style.left = safe + 'px';
        panel.style.top = safe + 'px';
        panel.style.maxHeight = '';

        const triggerRect = trigger.getBoundingClientRect();
        const panelWidth = panel.offsetWidth;

        let left = triggerRect.left;

        if (left + panelWidth > window.innerWidth - safe) {
            left = window.innerWidth - panelWidth - safe;
        }

        left = Math.max(safe, left);

        let top = triggerRect.bottom + gap;
        const availableBelow = window.innerHeight - top - safe;
        const availableAbove = triggerRect.top - gap - safe;

        if (availableBelow < 220 && availableAbove > availableBelow) {
            const wanted = Math.min(
                panel.scrollHeight,
                Math.max(220, availableAbove));

            top = Math.max(
                safe,
                triggerRect.top - gap - wanted);

            panel.style.maxHeight =
                Math.max(220, triggerRect.top - gap - safe) + 'px';
        }
        else {
            panel.style.maxHeight =
                Math.max(220, availableBelow) + 'px';
        }

        panel.style.left = Math.round(left) + 'px';
        panel.style.top = Math.round(top) + 'px';
        panel.style.visibility = '';
    };

    const closeDesktopPanels = () => {
        clearCloseTimer();
        openButton = null;
        openPanel = null;

        [
            ...menuButtons,
            ...(departmentsButton ? [departmentsButton] : [])
        ].forEach(button => {
            button.classList.remove('is-open');
            button.setAttribute('aria-expanded', 'false');
        });

        desktopPanels.forEach(panel => {
            panel.classList.remove('is-open');
            panel.setAttribute('aria-hidden', 'true');
            panel.style.left = '';
            panel.style.top = '';
            panel.style.maxHeight = '';
            panel.style.visibility = '';
        });
    };

    const showPanel = (trigger, panel) => {
        if (!trigger || !panel || isMobile()) return;

        clearCloseTimer();

        const same = openButton === trigger && openPanel === panel;

        if (!same) {
            closeDesktopPanels();
            openButton = trigger;
            openPanel = panel;

            trigger.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');

            panel.classList.add('is-open');
            panel.setAttribute('aria-hidden', 'false');
        }

        placePanel(trigger, panel);
    };

    const scheduleClose = () => {
        clearCloseTimer();
        closeTimer = window.setTimeout(closeDesktopPanels, 170);
    };

    menuButtons.forEach(button => {
        const id = button.dataset.nsqMenuButton;
        const panel = root.querySelector(
            `[data-nsq-menu-panel="${id}"]`);

        if (!panel) return;

        button.addEventListener(
            'mouseenter',
            () => showPanel(button, panel));

        button.addEventListener(
            'focus',
            () => showPanel(button, panel));

        button.addEventListener('mouseleave', scheduleClose);
    });

    if (departmentsButton && departmentsPanel) {
        departmentsButton.addEventListener(
            'mouseenter',
            () => showPanel(departmentsButton, departmentsPanel));

        departmentsButton.addEventListener(
            'focus',
            () => showPanel(departmentsButton, departmentsPanel));

        departmentsButton.addEventListener(
            'mouseleave',
            scheduleClose);

        departmentsButton.addEventListener('click', event => {
            if (isMobile()) return;

            event.preventDefault();

            if (openButton === departmentsButton &&
                openPanel === departmentsPanel) {
                closeDesktopPanels();
            }
            else {
                showPanel(departmentsButton, departmentsPanel);
            }
        });
    }

    desktopPanels.forEach(panel => {
        panel.addEventListener('mouseenter', clearCloseTimer);
        panel.addEventListener('mouseleave', scheduleClose);
    });

    const updateScrollButtons = () => {
        if (!strip || !scrollLeftButton || !scrollRightButton) return;

        const overflow =
            strip.scrollWidth > strip.clientWidth + 4;

        if (!overflow) {
            scrollLeftButton.hidden = true;
            scrollRightButton.hidden = true;
            return;
        }

        scrollLeftButton.hidden = strip.scrollLeft <= 2;

        scrollRightButton.hidden =
            strip.scrollLeft + strip.clientWidth >=
            strip.scrollWidth - 2;
    };

    if (strip) {
        strip.addEventListener(
            'scroll',
            updateScrollButtons,
            { passive: true });

        strip.addEventListener(
            'wheel',
            event => {
                const overflow =
                    strip.scrollWidth > strip.clientWidth + 4;

                if (!overflow) return;

                const vertical =
                    Math.abs(event.deltaY) > Math.abs(event.deltaX);

                if (!vertical) return;

                event.preventDefault();
                strip.scrollLeft += event.deltaY;
                updateScrollButtons();
            },
            { passive: false });

        scrollLeftButton?.addEventListener('click', () => {
            strip.scrollBy({
                left: -Math.max(240, strip.clientWidth * .60),
                behavior: 'smooth'
            });
        });

        scrollRightButton?.addEventListener('click', () => {
            strip.scrollBy({
                left: Math.max(240, strip.clientWidth * .60),
                behavior: 'smooth'
            });
        });
    }

    if (mobileToggle && mobilePanel) {
        mobileToggle.addEventListener('click', () => {
            const nextOpen = mobilePanel.hidden;
            mobilePanel.hidden = !nextOpen;
            mobileToggle.setAttribute(
                'aria-expanded',
                nextOpen ? 'true' : 'false');
        });
    }

    document.addEventListener('click', event => {
        if (!root.contains(event.target)) {
            closeDesktopPanels();

            if (mobilePanel && !mobilePanel.hidden) {
                mobilePanel.hidden = true;
                mobileToggle?.setAttribute(
                    'aria-expanded',
                    'false');
            }
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;

        closeDesktopPanels();

        if (mobilePanel) {
            mobilePanel.hidden = true;
        }

        mobileToggle?.setAttribute(
            'aria-expanded',
            'false');
    });

    const updateScrolledState = () => {
        document.documentElement.classList.toggle(
            'nsq-nav-scrolled',
            window.scrollY > 14);

        if (openButton && openPanel) {
            placePanel(openButton, openPanel);
        }
    };

    window.addEventListener(
        'scroll',
        updateScrolledState,
        { passive: true });

    window.addEventListener(
        'resize',
        () => {
            if (isMobile()) {
                closeDesktopPanels();
            }
            else {
                if (mobilePanel) {
                    mobilePanel.hidden = true;
                }

                mobileToggle?.setAttribute(
                    'aria-expanded',
                    'false');

                if (openButton && openPanel) {
                    placePanel(openButton, openPanel);
                }
            }

            updateScrollButtons();
        },
        { passive: true });

    if ('ResizeObserver' in window && strip) {
        const observer = new ResizeObserver(updateScrollButtons);
        observer.observe(strip);
    }

    updateScrolledState();
    window.requestAnimationFrame(updateScrollButtons);
})();


/* NSQ_GLOBAL_NAV_V4_3_ROUTE_IN_CONTENT_JS_BASE */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    if (!root || root.dataset.v41Ready === '1') return;

    root.dataset.v41Ready = '1';

    /* El texto de Claro/Oscuro no debe reservar espacio aunque una regla
       historica de _TopNavbar intente volver a mostrarlo. */
    document
        .querySelectorAll('.theme-switcher-text, #themeToggleText')
        .forEach(element => {
            element.hidden = true;
            element.setAttribute('aria-hidden', 'true');
            element.style.setProperty('display', 'none', 'important');
        });

    /* Almacen usa _AlmacenLayout y su propio alm-main. Marcamos ese layout
       para usar navbar fixed sin alterar los departamentos donde sticky ya
       funciona correctamente. */
    const isAlmacen = Boolean(document.querySelector('.alm-main'));

    if (!isAlmacen || !document.body) return;

    document.body.classList.add('nsq-layout-almacen');

    const host =
        document.querySelector('body > .nsq-unified-navbar-host') ||
        document.querySelector('body > .global-navbar-host') ||
        document.querySelector('body > .layout-top-navbar-host');

    if (!host) return;

    const syncNavbarHeight = () => {
        const height = Math.ceil(host.getBoundingClientRect().height);

        if (height > 0) {
            document.documentElement.style.setProperty(
                '--nsq-almacen-navbar-height',
                `${height}px`);
        }
    };

    syncNavbarHeight();
    window.requestAnimationFrame(syncNavbarHeight);
    window.addEventListener('resize', syncNavbarHeight, { passive: true });

    if ('ResizeObserver' in window) {
        const observer = new ResizeObserver(syncNavbarHeight);
        observer.observe(host);
    }
})();


/* NSQ_GLOBAL_NAV_V4_2_NAVIGATION_JS */
(() => {
    const root =
        document.querySelector(
            '[data-nsq-department-nav]'
        );

    if (!root || root.dataset.v42Ready === '1') {
        return;
    }

    root.dataset.v42Ready = '1';

    const initialize = () => {
        if (!document.body) {
            return;
        }

        document.body.classList.add(
            'nsq-unified-navbar-active'
        );

        const departmentsButton =
            root.querySelector(
                '[data-nsq-departments-button]'
            );

        const departmentsPanel =
            root.querySelector(
                '[data-nsq-departments-panel]'
            );

        const navbarHost =
            root.closest(
                '.nsq-unified-navbar-host, .global-navbar-host, .layout-top-navbar-host'
            )
            ||
            document.querySelector(
                '.nsq-unified-navbar-host, .global-navbar-host, .layout-top-navbar-host'
            );

        const navbarLeft =
            document.querySelector(
                '.top-navbar .navbar-left'
            );

        const navbarBrand =
            navbarLeft?.querySelector(
                '.navbar-brand'
            );

        /*
         * Departamentos queda junto al titulo del departamento y deja
         * completamente libre el extremo derecho del carril de menus.
         */
        if (
            departmentsButton
            &&
            navbarLeft
            &&
            departmentsButton.parentElement !== navbarLeft
        ) {
            departmentsButton.classList.add(
                'nsq-departments-launcher--brand'
            );

            if (navbarBrand) {
                navbarBrand.insertAdjacentElement(
                    'afterend',
                    departmentsButton
                );
            }
            else {
                navbarLeft.appendChild(
                    departmentsButton
                );
            }

            /*
             * El JS V4 original escucha clicks en document y considera
             * cualquier elemento fuera de root como click exterior.
             * Como este boton ahora vive al lado del titulo, detenemos la
             * propagacion solamente en el lanzador para no cerrarlo al abrir.
             */
            departmentsButton.addEventListener(
                'click',
                event => {
                    event.stopPropagation();
                }
            );
        }

        /*
         * Claro/Oscuro: garantizamos por JS que no quede ancho reservado
         * por estilos historicos de _TopNavbar.
         */
        document
            .querySelectorAll(
                'button.theme-switcher, .theme-switcher, button#themeToggle'
            )
            .forEach(button => {
                button.style.setProperty(
                    'width',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'min-width',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'max-width',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'height',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'min-height',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'padding',
                    '0',
                    'important'
                );

                button.style.setProperty(
                    'margin',
                    '0',
                    'important'
                );

                button.style.setProperty(
                    'gap',
                    '0',
                    'important'
                );

                button.style.setProperty(
                    'flex',
                    '0 0 38px',
                    'important'
                );
            });

        document
            .querySelectorAll(
                '.theme-switcher-text, #themeToggleText'
            )
            .forEach(text => {
                text.hidden = true;
                text.setAttribute(
                    'aria-hidden',
                    'true'
                );

                text.style.setProperty(
                    'display',
                    'none',
                    'important'
                );
            });

        /* =========================================================
           METRICAS / SCROLL
           ========================================================= */

        const currentPath =
            window.location.pathname || '/';

        const isAlmacen =
            /^\/Almacen/i.test(
                currentPath
            );

        if (isAlmacen) {
            document.body.classList.add(
                'nsq-layout-almacen'
            );
        }

        const syncNavbarMetrics = () => {
            if (!navbarHost) {
                return;
            }

            const height =
                Math.ceil(
                    navbarHost
                        .getBoundingClientRect()
                        .height
                );

            if (height <= 0) {
                return;
            }

            document.documentElement
                .style
                .setProperty(
                    '--nsq-navbar-current-height',
                    `${height}px`
                );
        };

        syncNavbarMetrics();

        window.requestAnimationFrame(
            syncNavbarMetrics
        );

        window.addEventListener(
            'resize',
            syncNavbarMetrics,
            { passive: true }
        );

        if (
            navbarHost
            &&
            'ResizeObserver' in window
        ) {
            const observer =
                new ResizeObserver(
                    syncNavbarMetrics
                );

            observer.observe(
                navbarHost
            );
        }

        /* =========================================================
           BREADCRUMB GLOBAL
           ========================================================= */

        const normalizePath = value => {
            if (!value) {
                return '';
            }

            let path = value
                .toString()
                .split('?')[0]
                .split('#')[0]
                .trim();

            if (path.length > 1) {
                path =
                    path.replace(
                        /\/+$/,
                        ''
                    );
            }

            return path.toLowerCase();
        };

        const friendlyAction = value => {
            if (!value) {
                return '';
            }

            const known = {
                Index: 'Inicio',
                Historico: 'Histórico',
                Historial: 'Historial',
                NivelesStock: 'Niveles de stock',
                CalendarioMaquinas: 'Calendario de máquinas',
                ProgramacionPersonal: 'Programación de personal',
                EnProceso: 'En proceso',
                ListaCarga: 'Lista de carga'
            };

            if (known[value]) {
                return known[value];
            }

            return value
                .replace(
                    /([a-z0-9])([A-Z])/g,
                    '$1 $2'
                )
                .replace(
                    /_/g,
                    ' '
                )
                .trim();
        };

        const activeDepartment =
            (
                root.dataset.activeDepartment
                ||
                ''
            ).trim();

        const activeDepartmentUrl =
            (
                root.dataset.activeDepartmentUrl
                ||
                ''
            ).trim();

        const activeMenu =
            (
                root.dataset.activeMenu
                ||
                ''
            ).trim();

        const activeMenuUrl =
            (
                root.dataset.activeMenuUrl
                ||
                ''
            ).trim();

        const activeSection =
            (
                root.dataset.activeSection
                ||
                ''
            ).trim();

        const activeSectionUrl =
            (
                root.dataset.activeSectionUrl
                ||
                ''
            ).trim();

        const currentAction =
            (
                root.dataset.currentAction
                ||
                ''
            ).trim();

        const routebar =
            document.createElement(
                'nav'
            );

        routebar.className =
            'nsq-global-routebar';

        routebar.setAttribute(
            'aria-label',
            'Ruta de navegación'
        );

        const backButton =
            document.createElement(
                'button'
            );

        backButton.type = 'button';
        backButton.className =
            'nsq-global-routebar__back';

        backButton.innerHTML =
            '<i class="fa-solid fa-arrow-left" aria-hidden="true"></i>'
            +
            '<span>Regresar</span>';

        const trail =
            document.createElement(
                'ol'
            );

        trail.className =
            'nsq-global-routebar__trail';

        routebar.appendChild(
            backButton
        );

        routebar.appendChild(
            trail
        );

        const crumbs = [];

        const addCrumb = (
            label,
            url,
            options = {}
        ) => {
            const cleanLabel =
                (label || '').trim();

            if (!cleanLabel) {
                return;
            }

            const normalizedUrl =
                normalizePath(url);

            const last =
                crumbs[
                    crumbs.length - 1
                ];

            if (
                last
                &&
                last.label
                    .toLowerCase() ===
                    cleanLabel.toLowerCase()
                &&
                normalizePath(last.url) ===
                    normalizedUrl
            ) {
                return;
            }

            crumbs.push({
                label: cleanLabel,
                url: url || '',
                current:
                    options.current === true
            });
        };

        addCrumb(
            'Inicio',
            '/Menu/Index'
        );

        if (activeDepartment) {
            addCrumb(
                activeDepartment,
                activeDepartmentUrl
            );
        }

        if (
            activeMenu
            &&
            normalizePath(activeMenuUrl) !==
                normalizePath(activeDepartmentUrl)
        ) {
            addCrumb(
                activeMenu,
                activeMenuUrl
            );
        }

        const currentNormalized =
            normalizePath(
                currentPath
            );

        const menuNormalized =
            normalizePath(
                activeMenuUrl
            );

        const sectionNormalized =
            normalizePath(
                activeSectionUrl
            );

        if (
            activeSection
            &&
            sectionNormalized
            &&
            sectionNormalized !==
                menuNormalized
        ) {
            addCrumb(
                activeSection,
                activeSectionUrl,
                {
                    current:
                        currentNormalized ===
                        sectionNormalized
                }
            );
        }

        const alreadyRepresentsCurrent =
            crumbs.some(
                crumb =>
                    normalizePath(
                        crumb.url
                    ) ===
                    currentNormalized
            );

        if (
            !alreadyRepresentsCurrent
            &&
            currentAction
            &&
            currentAction.toLowerCase() !==
                'index'
        ) {
            let pageLabel = '';

            const pageHeading =
                document.querySelector(
                    'main h1, .page-title h1, .page-header h1, h1'
                );

            if (pageHeading) {
                pageLabel =
                    (
                        pageHeading.textContent
                        ||
                        ''
                    ).trim();
            }

            if (
                !pageLabel
                ||
                pageLabel.length > 70
            ) {
                pageLabel =
                    friendlyAction(
                        currentAction
                    );
            }

            addCrumb(
                pageLabel,
                currentPath,
                { current: true }
            );
        }

        /*
         * Si la ultima ruta coincide con la URL actual, esa es la hoja
         * de la jerarquia y deja de ser enlace.
         */
        if (crumbs.length > 0) {
            crumbs.forEach(
                crumb => {
                    crumb.current = false;
                }
            );

            let currentIndex =
                crumbs.findIndex(
                    crumb =>
                        normalizePath(
                            crumb.url
                        ) ===
                        currentNormalized
                );

            if (currentIndex < 0) {
                currentIndex =
                    crumbs.length - 1;
            }

            crumbs[
                currentIndex
            ].current = true;
        }

        crumbs.forEach(
            (crumb, index) => {
                if (index > 0) {
                    const separator =
                        document.createElement(
                            'li'
                        );

                    separator.className =
                        'nsq-global-routebar__separator';

                    separator.setAttribute(
                        'aria-hidden',
                        'true'
                    );

                    separator.textContent =
                        '›';

                    trail.appendChild(
                        separator
                    );
                }

                const item =
                    document.createElement(
                        'li'
                    );

                item.className =
                    'nsq-global-routebar__item';

                if (crumb.current) {
                    item.classList.add(
                        'nsq-global-routebar__item--current'
                    );

                    item.setAttribute(
                        'aria-current',
                        'page'
                    );

                    item.textContent =
                        crumb.label;
                }
                else {
                    const link =
                        document.createElement(
                            'a'
                        );

                    link.href =
                        crumb.url || '#';

                    link.textContent =
                        crumb.label;

                    item.appendChild(
                        link
                    );
                }

                trail.appendChild(
                    item
                );
            }
        );

        const currentIndex =
            crumbs.findIndex(
                crumb =>
                    crumb.current
            );

        const previous =
            currentIndex > 0
                ? crumbs[
                    currentIndex - 1
                ]
                : crumbs[0];

        const backTarget =
            previous?.url
            ||
            activeDepartmentUrl
            ||
            '/Menu/Index';

        backButton.addEventListener(
            'click',
            () => {
                window.location.assign(
                    backTarget
                );
            }
        );

        /*
         * Sustituimos cualquier barra global anterior y ocultamos la
         * ruta antigua de Almacen para que exista una sola jerarquia.
         */
        document
            .querySelectorAll(
                '.nsq-global-routebar'
            )
            .forEach(existing => {
                if (existing !== routebar) {
                    existing.remove();
                }
            });

        if (navbarHost) {
            navbarHost.insertAdjacentElement(
                'afterend',
                routebar
            );
        }
        else {
            document.body.insertBefore(
                routebar,
                document.body.firstChild
            );
        }

        document
            .querySelectorAll(
                '.alm-routebar'
            )
            .forEach(oldRoute => {
                oldRoute.hidden = true;
                oldRoute.setAttribute(
                    'aria-hidden',
                    'true'
                );
            });

        /*
         * Almacen: la deteccion ya no depende de que .alm-main exista
         * cuando se ejecuta el script. El host se obtiene desde root y
         * queda fixed durante todo el scroll.
         */
        if (
            isAlmacen
            &&
            navbarHost
        ) {
            navbarHost.style.setProperty(
                'position',
                'fixed',
                'important'
            );

            navbarHost.style.setProperty(
                'top',
                '0',
                'important'
            );

            navbarHost.style.setProperty(
                'left',
                '0',
                'important'
            );

            navbarHost.style.setProperty(
                'right',
                '0',
                'important'
            );

            navbarHost.style.setProperty(
                'width',
                '100%',
                'important'
            );

            navbarHost.style.setProperty(
                'z-index',
                '3000',
                'important'
            );

            syncNavbarMetrics();
        }

        /*
         * Al mover Departamentos fuera de root, el panel V4 original
         * sigue funcionando con el mismo elemento y la misma referencia.
         * En resize actualizamos sus metricas.
         */
        if (
            departmentsButton
            &&
            departmentsPanel
        ) {
            departmentsButton.setAttribute(
                'title',
                'Departamentos'
            );

            departmentsButton.setAttribute(
                'aria-label',
                'Abrir departamentos'
            );
        }
    };

    if (
        document.readyState ===
        'loading'
    ) {
        document.addEventListener(
            'DOMContentLoaded',
            initialize,
            { once: true }
        );
    }
    else {
        initialize();
    }
})();


/* NSQ_GLOBAL_NAV_V4_3_ROUTE_IN_CONTENT_JS */
(() => {
    const root =
        document.querySelector(
            '[data-nsq-department-nav]'
        );

    if (!root || root.dataset.v43Ready === '1') {
        return;
    }

    root.dataset.v43Ready = '1';

    const initialize = () => {
        if (!document.body) {
            return;
        }

        const navbarLeft =
            document.querySelector(
                '.top-navbar .navbar-left'
            );

        const navbarBrand =
            navbarLeft?.querySelector(
                '.navbar-brand'
            );

        const departmentsButton =
            document.querySelector(
                '[data-nsq-departments-button]'
            );

        /*
         * ALMACEN [icono] se vuelve una sola unidad visual.
         * El boton sigue siendo hermano del <a>, no se crea HTML invalido.
         */
        if (
            navbarLeft
            &&
            navbarBrand
            &&
            departmentsButton
        ) {
            let cluster =
                navbarLeft.querySelector(
                    '.nsq-brand-cluster'
                );

            if (!cluster) {
                cluster =
                    document.createElement(
                        'div'
                    );

                cluster.className =
                    'nsq-brand-cluster';

                navbarLeft.insertBefore(
                    cluster,
                    navbarBrand
                );

                cluster.appendChild(
                    navbarBrand
                );
            }

            if (
                departmentsButton.parentElement
                !==
                cluster
            ) {
                cluster.appendChild(
                    departmentsButton
                );
            }

            departmentsButton.classList.add(
                'nsq-departments-launcher--brand'
            );
        }

        /*
         * Esperamos a que V4.2 haya creado la ruta global y entonces
         * la llevamos al CONTENIDO. Ya no se queda debajo del navbar.
         */
        const relocateRoutebar = () => {
            const routebar =
                document.querySelector(
                    '.nsq-global-routebar'
                );

            if (!routebar) {
                return false;
            }

            let contentHost = null;

            if (
                document.body.classList.contains(
                    'nsq-layout-almacen'
                )
                ||
                /^\/Almacen/i.test(
                    window.location.pathname
                )
            ) {
                contentHost =
                    document.querySelector(
                        '.alm-main'
                    );
            }

            contentHost =
                contentHost
                ||
                document.querySelector(
                    '.global-render-host'
                )
                ||
                document.querySelector(
                    '.layout-render-host'
                )
                ||
                document.querySelector(
                    'main[role="main"]'
                )
                ||
                document.querySelector(
                    'main'
                );

            if (!contentHost) {
                return false;
            }

            if (
                routebar.parentElement
                !==
                contentHost
            ) {
                contentHost.insertBefore(
                    routebar,
                    contentHost.firstChild
                );
            }

            routebar.dataset.routePlacement =
                'content';

            return true;
        };

        /*
         * Hay layouts donde V4.2 termina en el mismo DOMContentLoaded.
         * Hacemos varios intentos cortos, sin polling permanente.
         */
        let attempts = 0;

        const tryRelocate = () => {
            attempts += 1;

            if (
                relocateRoutebar()
                ||
                attempts >= 8
            ) {
                return;
            }

            window.setTimeout(
                tryRelocate,
                30
            );
        };

        window.requestAnimationFrame(
            tryRelocate
        );

        /*
         * Las dos rutas heredadas de Almacén se eliminan visualmente
         * también desde JS por si un CSS antiguo tiene mayor prioridad.
         */
        document
            .querySelectorAll(
                '.alm-routebar, .alm-module-route, .alm-module-nav'
            )
            .forEach(oldRoute => {
                oldRoute.hidden = true;

                oldRoute.setAttribute(
                    'aria-hidden',
                    'true'
                );

                oldRoute.style.setProperty(
                    'display',
                    'none',
                    'important'
                );
            });
    };

    if (
        document.readyState ===
        'loading'
    ) {
        document.addEventListener(
            'DOMContentLoaded',
            initialize,
            { once: true }
        );
    }
    else {
        initialize();
    }
})();


/* NSQ_GLOBAL_NAV_V4_4_UNIFICAR_RUTAS_JS */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    if (!root || root.dataset.v44Ready === '1') {
        return;
    }

    root.dataset.v44Ready = '1';

    const initialize = () => {
        if (!document.body) {
            return;
        }

        const rawPath = window.location.pathname || '/';
        const path = rawPath.replace(/\/+$/, '') || '/';

        const isMenuIndex = /^\/Menu\/Index$/i.test(path) || /^\/Menu$/i.test(path);
        const isMenuGroup = /^\/Menu\/Grupo(?:\/|$)/i.test(path);
        const suppressRoutebar = isMenuIndex || isMenuGroup;

        if (isMenuIndex) {
            document.body.classList.add('nsq-menu-index-page');
        }

        if (isMenuGroup) {
            document.body.classList.add('nsq-menu-group-page');
        }

        if (suppressRoutebar) {
            document.body.classList.add('nsq-routebar-suppressed');
        }

        const removeSuppressedRoutebar = () => {
            if (!suppressRoutebar) {
                return false;
            }

            let removed = false;

            document
                .querySelectorAll('.nsq-global-routebar')
                .forEach(route => {
                    route.remove();
                    removed = true;
                });

/* NSQ_GLOBAL_NAV_V4_JS */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    if (!root || root.dataset.v4Ready === '1') return;

    root.dataset.v4Ready = '1';
    document.body?.classList.add('nsq-unified-navbar-active');

    const activeDepartment = (root.dataset.activeDepartment || '').trim();
    const brandTitle = document.querySelector(
        '.top-navbar .nsq-brand-title, .top-navbar .navbar-title');

    if (brandTitle) {
        brandTitle.textContent = activeDepartment || 'NS QUELL - ERP';
        brandTitle.setAttribute('title', brandTitle.textContent);
    }

    const strip = root.querySelector('[data-nsq-context-strip]');
    const scrollLeftButton = root.querySelector('[data-nsq-scroll-left]');
    const scrollRightButton = root.querySelector('[data-nsq-scroll-right]');

    const menuButtons = Array.from(
        root.querySelectorAll('[data-nsq-menu-button]'));
    const menuPanels = Array.from(
        root.querySelectorAll('[data-nsq-menu-panel]'));

    const departmentsButton =
        root.querySelector('[data-nsq-departments-button]');
    const departmentsPanel =
        root.querySelector('[data-nsq-departments-panel]');

    const mobileToggle = root.querySelector('[data-nsq-mobile-toggle]');
    const mobilePanel = root.querySelector('[data-nsq-mobile-panel]');

    const desktopPanels = [
        ...menuPanels,
        ...(departmentsPanel ? [departmentsPanel] : [])
    ];

    let closeTimer = null;
    let openButton = null;
    let openPanel = null;

    const isMobile = () =>
        window.matchMedia('(max-width: 960px)').matches;

    const clearCloseTimer = () => {
        if (!closeTimer) return;
        window.clearTimeout(closeTimer);
        closeTimer = null;
    };

    const placePanel = (trigger, panel) => {
        if (!trigger || !panel || !panel.classList.contains('is-open')) {
            return;
        }

        const safe = 12;
        const gap = 6;

        panel.style.visibility = 'hidden';
        panel.style.left = safe + 'px';
        panel.style.top = safe + 'px';
        panel.style.maxHeight = '';

        const triggerRect = trigger.getBoundingClientRect();
        const panelWidth = panel.offsetWidth;

        let left = triggerRect.left;

        if (left + panelWidth > window.innerWidth - safe) {
            left = window.innerWidth - panelWidth - safe;
        }

        left = Math.max(safe, left);

        let top = triggerRect.bottom + gap;
        const availableBelow = window.innerHeight - top - safe;
        const availableAbove = triggerRect.top - gap - safe;

        if (availableBelow < 220 && availableAbove > availableBelow) {
            const wanted = Math.min(
                panel.scrollHeight,
                Math.max(220, availableAbove));

            top = Math.max(
                safe,
                triggerRect.top - gap - wanted);

            panel.style.maxHeight =
                Math.max(220, triggerRect.top - gap - safe) + 'px';
        }
        else {
            panel.style.maxHeight =
                Math.max(220, availableBelow) + 'px';
        }

        panel.style.left = Math.round(left) + 'px';
        panel.style.top = Math.round(top) + 'px';
        panel.style.visibility = '';
    };

    const closeDesktopPanels = () => {
        clearCloseTimer();
        openButton = null;
        openPanel = null;

        [
            ...menuButtons,
            ...(departmentsButton ? [departmentsButton] : [])
        ].forEach(button => {
            button.classList.remove('is-open');
            button.setAttribute('aria-expanded', 'false');
        });

        desktopPanels.forEach(panel => {
            panel.classList.remove('is-open');
            panel.setAttribute('aria-hidden', 'true');
            panel.style.left = '';
            panel.style.top = '';
            panel.style.maxHeight = '';
            panel.style.visibility = '';
        });
    };

    const showPanel = (trigger, panel) => {
        if (!trigger || !panel || isMobile()) return;

        clearCloseTimer();

        const same = openButton === trigger && openPanel === panel;

        if (!same) {
            closeDesktopPanels();
            openButton = trigger;
            openPanel = panel;

            trigger.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');

            panel.classList.add('is-open');
            panel.setAttribute('aria-hidden', 'false');
        }

        placePanel(trigger, panel);
    };

    const scheduleClose = () => {
        clearCloseTimer();
        closeTimer = window.setTimeout(closeDesktopPanels, 170);
    };

    menuButtons.forEach(button => {
        const id = button.dataset.nsqMenuButton;
        const panel = root.querySelector(
            `[data-nsq-menu-panel="${id}"]`);

        if (!panel) return;

        button.addEventListener(
            'mouseenter',
            () => showPanel(button, panel));

        button.addEventListener(
            'focus',
            () => showPanel(button, panel));

        button.addEventListener('mouseleave', scheduleClose);
    });

    if (departmentsButton && departmentsPanel) {
        departmentsButton.addEventListener(
            'mouseenter',
            () => showPanel(departmentsButton, departmentsPanel));

        departmentsButton.addEventListener(
            'focus',
            () => showPanel(departmentsButton, departmentsPanel));

        departmentsButton.addEventListener(
            'mouseleave',
            scheduleClose);

        departmentsButton.addEventListener('click', event => {
            if (isMobile()) return;

            event.preventDefault();

            if (openButton === departmentsButton &&
                openPanel === departmentsPanel) {
                closeDesktopPanels();
            }
            else {
                showPanel(departmentsButton, departmentsPanel);
            }
        });
    }

    desktopPanels.forEach(panel => {
        panel.addEventListener('mouseenter', clearCloseTimer);
        panel.addEventListener('mouseleave', scheduleClose);
    });

    const updateScrollButtons = () => {
        if (!strip || !scrollLeftButton || !scrollRightButton) return;

        const overflow =
            strip.scrollWidth > strip.clientWidth + 4;

        if (!overflow) {
            scrollLeftButton.hidden = true;
            scrollRightButton.hidden = true;
            return;
        }

        scrollLeftButton.hidden = strip.scrollLeft <= 2;

        scrollRightButton.hidden =
            strip.scrollLeft + strip.clientWidth >=
            strip.scrollWidth - 2;
    };

    if (strip) {
        strip.addEventListener(
            'scroll',
            updateScrollButtons,
            { passive: true });

        strip.addEventListener(
            'wheel',
            event => {
                const overflow =
                    strip.scrollWidth > strip.clientWidth + 4;

                if (!overflow) return;

                const vertical =
                    Math.abs(event.deltaY) > Math.abs(event.deltaX);

                if (!vertical) return;

                event.preventDefault();
                strip.scrollLeft += event.deltaY;
                updateScrollButtons();
            },
            { passive: false });

        scrollLeftButton?.addEventListener('click', () => {
            strip.scrollBy({
                left: -Math.max(240, strip.clientWidth * .60),
                behavior: 'smooth'
            });
        });

        scrollRightButton?.addEventListener('click', () => {
            strip.scrollBy({
                left: Math.max(240, strip.clientWidth * .60),
                behavior: 'smooth'
            });
        });
    }

    if (mobileToggle && mobilePanel) {
        mobileToggle.addEventListener('click', () => {
            const nextOpen = mobilePanel.hidden;
            mobilePanel.hidden = !nextOpen;
            mobileToggle.setAttribute(
                'aria-expanded',
                nextOpen ? 'true' : 'false');
        });
    }

    document.addEventListener('click', event => {
        if (!root.contains(event.target)) {
            closeDesktopPanels();

            if (mobilePanel && !mobilePanel.hidden) {
                mobilePanel.hidden = true;
                mobileToggle?.setAttribute(
                    'aria-expanded',
                    'false');
            }
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;

        closeDesktopPanels();

        if (mobilePanel) {
            mobilePanel.hidden = true;
        }

        mobileToggle?.setAttribute(
            'aria-expanded',
            'false');
    });

    const updateScrolledState = () => {
        document.documentElement.classList.toggle(
            'nsq-nav-scrolled',
            window.scrollY > 14);

        if (openButton && openPanel) {
            placePanel(openButton, openPanel);
        }
    };

    window.addEventListener(
        'scroll',
        updateScrolledState,
        { passive: true });

    window.addEventListener(
        'resize',
        () => {
            if (isMobile()) {
                closeDesktopPanels();
            }
            else {
                if (mobilePanel) {
                    mobilePanel.hidden = true;
                }

                mobileToggle?.setAttribute(
                    'aria-expanded',
                    'false');

                if (openButton && openPanel) {
                    placePanel(openButton, openPanel);
                }
            }

            updateScrollButtons();
        },
        { passive: true });

    if ('ResizeObserver' in window && strip) {
        const observer = new ResizeObserver(updateScrollButtons);
        observer.observe(strip);
    }

    updateScrolledState();
    window.requestAnimationFrame(updateScrollButtons);
})();


/* NSQ_GLOBAL_NAV_V4_3_ROUTE_IN_CONTENT_JS_BASE */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    if (!root || root.dataset.v41Ready === '1') return;

    root.dataset.v41Ready = '1';

    /* El texto de Claro/Oscuro no debe reservar espacio aunque una regla
       historica de _TopNavbar intente volver a mostrarlo. */
    document
        .querySelectorAll('.theme-switcher-text, #themeToggleText')
        .forEach(element => {
            element.hidden = true;
            element.setAttribute('aria-hidden', 'true');
            element.style.setProperty('display', 'none', 'important');
        });

    /* Almacen usa _AlmacenLayout y su propio alm-main. Marcamos ese layout
       para usar navbar fixed sin alterar los departamentos donde sticky ya
       funciona correctamente. */
    const isAlmacen = Boolean(document.querySelector('.alm-main'));

    if (!isAlmacen || !document.body) return;

    document.body.classList.add('nsq-layout-almacen');

    const host =
        document.querySelector('body > .nsq-unified-navbar-host') ||
        document.querySelector('body > .global-navbar-host') ||
        document.querySelector('body > .layout-top-navbar-host');

    if (!host) return;

    const syncNavbarHeight = () => {
        const height = Math.ceil(host.getBoundingClientRect().height);

        if (height > 0) {
            document.documentElement.style.setProperty(
                '--nsq-almacen-navbar-height',
                `${height}px`);
        }
    };

    syncNavbarHeight();
    window.requestAnimationFrame(syncNavbarHeight);
    window.addEventListener('resize', syncNavbarHeight, { passive: true });

    if ('ResizeObserver' in window) {
        const observer = new ResizeObserver(syncNavbarHeight);
        observer.observe(host);
    }
})();


/* NSQ_GLOBAL_NAV_V4_2_NAVIGATION_JS */
(() => {
    const root =
        document.querySelector(
            '[data-nsq-department-nav]'
        );

    if (!root || root.dataset.v42Ready === '1') {
        return;
    }

    root.dataset.v42Ready = '1';

    const initialize = () => {
        if (!document.body) {
            return;
        }

        document.body.classList.add(
            'nsq-unified-navbar-active'
        );

        const departmentsButton =
            root.querySelector(
                '[data-nsq-departments-button]'
            );

        const departmentsPanel =
            root.querySelector(
                '[data-nsq-departments-panel]'
            );

        const navbarHost =
            root.closest(
                '.nsq-unified-navbar-host, .global-navbar-host, .layout-top-navbar-host'
            )
            ||
            document.querySelector(
                '.nsq-unified-navbar-host, .global-navbar-host, .layout-top-navbar-host'
            );

        const navbarLeft =
            document.querySelector(
                '.top-navbar .navbar-left'
            );

        const navbarBrand =
            navbarLeft?.querySelector(
                '.navbar-brand'
            );

        /*
         * Departamentos queda junto al titulo del departamento y deja
         * completamente libre el extremo derecho del carril de menus.
         */
        if (
            departmentsButton
            &&
            navbarLeft
            &&
            departmentsButton.parentElement !== navbarLeft
        ) {
            departmentsButton.classList.add(
                'nsq-departments-launcher--brand'
            );

            if (navbarBrand) {
                navbarBrand.insertAdjacentElement(
                    'afterend',
                    departmentsButton
                );
            }
            else {
                navbarLeft.appendChild(
                    departmentsButton
                );
            }

            /*
             * El JS V4 original escucha clicks en document y considera
             * cualquier elemento fuera de root como click exterior.
             * Como este boton ahora vive al lado del titulo, detenemos la
             * propagacion solamente en el lanzador para no cerrarlo al abrir.
             */
            departmentsButton.addEventListener(
                'click',
                event => {
                    event.stopPropagation();
                }
            );
        }

        /*
         * Claro/Oscuro: garantizamos por JS que no quede ancho reservado
         * por estilos historicos de _TopNavbar.
         */
        document
            .querySelectorAll(
                'button.theme-switcher, .theme-switcher, button#themeToggle'
            )
            .forEach(button => {
                button.style.setProperty(
                    'width',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'min-width',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'max-width',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'height',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'min-height',
                    '38px',
                    'important'
                );

                button.style.setProperty(
                    'padding',
                    '0',
                    'important'
                );

                button.style.setProperty(
                    'margin',
                    '0',
                    'important'
                );

                button.style.setProperty(
                    'gap',
                    '0',
                    'important'
                );

                button.style.setProperty(
                    'flex',
                    '0 0 38px',
                    'important'
                );
            });

        document
            .querySelectorAll(
                '.theme-switcher-text, #themeToggleText'
            )
            .forEach(text => {
                text.hidden = true;
                text.setAttribute(
                    'aria-hidden',
                    'true'
                );

                text.style.setProperty(
                    'display',
                    'none',
                    'important'
                );
            });

        /* =========================================================
           METRICAS / SCROLL
           ========================================================= */

        const currentPath =
            window.location.pathname || '/';

        const isAlmacen =
            /^\/Almacen/i.test(
                currentPath
            );

        if (isAlmacen) {
            document.body.classList.add(
                'nsq-layout-almacen'
            );
        }

        const syncNavbarMetrics = () => {
            if (!navbarHost) {
                return;
            }

            const height =
                Math.ceil(
                    navbarHost
                        .getBoundingClientRect()
                        .height
                );

            if (height <= 0) {
                return;
            }

            document.documentElement
                .style
                .setProperty(
                    '--nsq-navbar-current-height',
                    `${height}px`
                );
        };

        syncNavbarMetrics();

        window.requestAnimationFrame(
            syncNavbarMetrics
        );

        window.addEventListener(
            'resize',
            syncNavbarMetrics,
            { passive: true }
        );

        if (
            navbarHost
            &&
            'ResizeObserver' in window
        ) {
            const observer =
                new ResizeObserver(
                    syncNavbarMetrics
                );

            observer.observe(
                navbarHost
            );
        }

        /* =========================================================
           BREADCRUMB GLOBAL
           ========================================================= */

        const normalizePath = value => {
            if (!value) {
                return '';
            }

            let path = value
                .toString()
                .split('?')[0]
                .split('#')[0]
                .trim();

            if (path.length > 1) {
                path =
                    path.replace(
                        /\/+$/,
                        ''
                    );
            }

            return path.toLowerCase();
        };

        const friendlyAction = value => {
            if (!value) {
                return '';
            }

            const known = {
                Index: 'Inicio',
                Historico: 'Histórico',
                Historial: 'Historial',
                NivelesStock: 'Niveles de stock',
                CalendarioMaquinas: 'Calendario de máquinas',
                ProgramacionPersonal: 'Programación de personal',
                EnProceso: 'En proceso',
                ListaCarga: 'Lista de carga'
            };

            if (known[value]) {
                return known[value];
            }

            return value
                .replace(
                    /([a-z0-9])([A-Z])/g,
                    '$1 $2'
                )
                .replace(
                    /_/g,
                    ' '
                )
                .trim();
        };

        const activeDepartment =
            (
                root.dataset.activeDepartment
                ||
                ''
            ).trim();

        const activeDepartmentUrl =
            (
                root.dataset.activeDepartmentUrl
                ||
                ''
            ).trim();

        const activeMenu =
            (
                root.dataset.activeMenu
                ||
                ''
            ).trim();

        const activeMenuUrl =
            (
                root.dataset.activeMenuUrl
                ||
                ''
            ).trim();

        const activeSection =
            (
                root.dataset.activeSection
                ||
                ''
            ).trim();

        const activeSectionUrl =
            (
                root.dataset.activeSectionUrl
                ||
                ''
            ).trim();

        const currentAction =
            (
                root.dataset.currentAction
                ||
                ''
            ).trim();

        const routebar =
            document.createElement(
                'nav'
            );

        routebar.className =
            'nsq-global-routebar';

        routebar.setAttribute(
            'aria-label',
            'Ruta de navegación'
        );

        const backButton =
            document.createElement(
                'button'
            );

        backButton.type = 'button';
        backButton.className =
            'nsq-global-routebar__back';

        backButton.innerHTML =
            '<i class="fa-solid fa-arrow-left" aria-hidden="true"></i>'
            +
            '<span>Regresar</span>';

        const trail =
            document.createElement(
                'ol'
            );

        trail.className =
            'nsq-global-routebar__trail';

        routebar.appendChild(
            backButton
        );

        routebar.appendChild(
            trail
        );

        const crumbs = [];

        const addCrumb = (
            label,
            url,
            options = {}
        ) => {
            const cleanLabel =
                (label || '').trim();

            if (!cleanLabel) {
                return;
            }

            const normalizedUrl =
                normalizePath(url);

            const last =
                crumbs[
                    crumbs.length - 1
                ];

            if (
                last
                &&
                last.label
                    .toLowerCase() ===
                    cleanLabel.toLowerCase()
                &&
                normalizePath(last.url) ===
                    normalizedUrl
            ) {
                return;
            }

            crumbs.push({
                label: cleanLabel,
                url: url || '',
                current:
                    options.current === true
            });
        };

        addCrumb(
            'Inicio',
            '/Menu/Index'
        );

        if (activeDepartment) {
            addCrumb(
                activeDepartment,
                activeDepartmentUrl
            );
        }

        if (
            activeMenu
            &&
            normalizePath(activeMenuUrl) !==
                normalizePath(activeDepartmentUrl)
        ) {
            addCrumb(
                activeMenu,
                activeMenuUrl
            );
        }

        const currentNormalized =
            normalizePath(
                currentPath
            );

        const menuNormalized =
            normalizePath(
                activeMenuUrl
            );

        const sectionNormalized =
            normalizePath(
                activeSectionUrl
            );

        if (
            activeSection
            &&
            sectionNormalized
            &&
            sectionNormalized !==
                menuNormalized
        ) {
            addCrumb(
                activeSection,
                activeSectionUrl,
                {
                    current:
                        currentNormalized ===
                        sectionNormalized
                }
            );
        }

        const alreadyRepresentsCurrent =
            crumbs.some(
                crumb =>
                    normalizePath(
                        crumb.url
                    ) ===
                    currentNormalized
            );

        if (
            !alreadyRepresentsCurrent
            &&
            currentAction
            &&
            currentAction.toLowerCase() !==
                'index'
        ) {
            let pageLabel = '';

            const pageHeading =
                document.querySelector(
                    'main h1, .page-title h1, .page-header h1, h1'
                );

            if (pageHeading) {
                pageLabel =
                    (
                        pageHeading.textContent
                        ||
                        ''
                    ).trim();
            }

            if (
                !pageLabel
                ||
                pageLabel.length > 70
            ) {
                pageLabel =
                    friendlyAction(
                        currentAction
                    );
            }

            addCrumb(
                pageLabel,
                currentPath,
                { current: true }
            );
        }

        /*
         * Si la ultima ruta coincide con la URL actual, esa es la hoja
         * de la jerarquia y deja de ser enlace.
         */
        if (crumbs.length > 0) {
            crumbs.forEach(
                crumb => {
                    crumb.current = false;
                }
            );

            let currentIndex =
                crumbs.findIndex(
                    crumb =>
                        normalizePath(
                            crumb.url
                        ) ===
                        currentNormalized
                );

            if (currentIndex < 0) {
                currentIndex =
                    crumbs.length - 1;
            }

            crumbs[
                currentIndex
            ].current = true;
        }

        crumbs.forEach(
            (crumb, index) => {
                if (index > 0) {
                    const separator =
                        document.createElement(
                            'li'
                        );

                    separator.className =
                        'nsq-global-routebar__separator';

                    separator.setAttribute(
                        'aria-hidden',
                        'true'
                    );

                    separator.textContent =
                        '›';

                    trail.appendChild(
                        separator
                    );
                }

                const item =
                    document.createElement(
                        'li'
                    );

                item.className =
                    'nsq-global-routebar__item';

                if (crumb.current) {
                    item.classList.add(
                        'nsq-global-routebar__item--current'
                    );

                    item.setAttribute(
                        'aria-current',
                        'page'
                    );

                    item.textContent =
                        crumb.label;
                }
                else {
                    const link =
                        document.createElement(
                            'a'
                        );

                    link.href =
                        crumb.url || '#';

                    link.textContent =
                        crumb.label;

                    item.appendChild(
                        link
                    );
                }

                trail.appendChild(
                    item
                );
            }
        );

        const currentIndex =
            crumbs.findIndex(
                crumb =>
                    crumb.current
            );

        const previous =
            currentIndex > 0
                ? crumbs[
                    currentIndex - 1
                ]
                : crumbs[0];

        const backTarget =
            previous?.url
            ||
            activeDepartmentUrl
            ||
            '/Menu/Index';

        backButton.addEventListener(
            'click',
            () => {
                window.location.assign(
                    backTarget
                );
            }
        );

        /*
         * Sustituimos cualquier barra global anterior y ocultamos la
         * ruta antigua de Almacen para que exista una sola jerarquia.
         */
        document
            .querySelectorAll(
                '.nsq-global-routebar'
            )
            .forEach(existing => {
                if (existing !== routebar) {
                    existing.remove();
                }
            });

        if (navbarHost) {
            navbarHost.insertAdjacentElement(
                'afterend',
                routebar
            );
        }
        else {
            document.body.insertBefore(
                routebar,
                document.body.firstChild
            );
        }

        document
            .querySelectorAll(
                '.alm-routebar'
            )
            .forEach(oldRoute => {
                oldRoute.hidden = true;
                oldRoute.setAttribute(
                    'aria-hidden',
                    'true'
                );
            });

        /*
         * Almacen: la deteccion ya no depende de que .alm-main exista
         * cuando se ejecuta el script. El host se obtiene desde root y
         * queda fixed durante todo el scroll.
         */
        if (
            isAlmacen
            &&
            navbarHost
        ) {
            navbarHost.style.setProperty(
                'position',
                'fixed',
                'important'
            );

            navbarHost.style.setProperty(
                'top',
                '0',
                'important'
            );

            navbarHost.style.setProperty(
                'left',
                '0',
                'important'
            );

            navbarHost.style.setProperty(
                'right',
                '0',
                'important'
            );

            navbarHost.style.setProperty(
                'width',
                '100%',
                'important'
            );

            navbarHost.style.setProperty(
                'z-index',
                '3000',
                'important'
            );

            syncNavbarMetrics();
        }

        /*
         * Al mover Departamentos fuera de root, el panel V4 original
         * sigue funcionando con el mismo elemento y la misma referencia.
         * En resize actualizamos sus metricas.
         */
        if (
            departmentsButton
            &&
            departmentsPanel
        ) {
            departmentsButton.setAttribute(
                'title',
                'Departamentos'
            );

            departmentsButton.setAttribute(
                'aria-label',
                'Abrir departamentos'
            );
        }
    };

    if (
        document.readyState ===
        'loading'
    ) {
        document.addEventListener(
            'DOMContentLoaded',
            initialize,
            { once: true }
        );
    }
    else {
        initialize();
    }
})();


/* NSQ_GLOBAL_NAV_V4_3_ROUTE_IN_CONTENT_JS */
(() => {
    const root =
        document.querySelector(
            '[data-nsq-department-nav]'
        );

    if (!root || root.dataset.v43Ready === '1') {
        return;
    }

    root.dataset.v43Ready = '1';

    const initialize = () => {
        if (!document.body) {
            return;
        }

        const navbarLeft =
            document.querySelector(
                '.top-navbar .navbar-left'
            );

        const navbarBrand =
            navbarLeft?.querySelector(
                '.navbar-brand'
            );

        const departmentsButton =
            document.querySelector(
                '[data-nsq-departments-button]'
            );

        /*
         * ALMACEN [icono] se vuelve una sola unidad visual.
         * El boton sigue siendo hermano del <a>, no se crea HTML invalido.
         */
        if (
            navbarLeft
            &&
            navbarBrand
            &&
            departmentsButton
        ) {
            let cluster =
                navbarLeft.querySelector(
                    '.nsq-brand-cluster'
                );

            if (!cluster) {
                cluster =
                    document.createElement(
                        'div'
                    );

                cluster.className =
                    'nsq-brand-cluster';

                navbarLeft.insertBefore(
                    cluster,
                    navbarBrand
                );

                cluster.appendChild(
                    navbarBrand
                );
            }

            if (
                departmentsButton.parentElement
                !==
                cluster
            ) {
                cluster.appendChild(
                    departmentsButton
                );
            }

            departmentsButton.classList.add(
                'nsq-departments-launcher--brand'
            );
        }

        /*
         * Esperamos a que V4.2 haya creado la ruta global y entonces
         * la llevamos al CONTENIDO. Ya no se queda debajo del navbar.
         */
        const relocateRoutebar = () => {
            const routebar =
                document.querySelector(
                    '.nsq-global-routebar'
                );

            if (!routebar) {
                return false;
            }

            let contentHost = null;

            if (
                document.body.classList.contains(
                    'nsq-layout-almacen'
                )
                ||
                /^\/Almacen/i.test(
                    window.location.pathname
                )
            ) {
                contentHost =
                    document.querySelector(
                        '.alm-main'
                    );
            }

            contentHost =
                contentHost
                ||
                document.querySelector(
                    '.global-render-host'
                )
                ||
                document.querySelector(
                    '.layout-render-host'
                )
                ||
                document.querySelector(
                    'main[role="main"]'
                )
                ||
                document.querySelector(
                    'main'
                );

            if (!contentHost) {
                return false;
            }

            if (
                routebar.parentElement
                !==
                contentHost
            ) {
                contentHost.insertBefore(
                    routebar,
                    contentHost.firstChild
                );
            }

            routebar.dataset.routePlacement =
                'content';

            return true;
        };

        /*
         * Hay layouts donde V4.2 termina en el mismo DOMContentLoaded.
         * Hacemos varios intentos cortos, sin polling permanente.
         */
        let attempts = 0;

        const tryRelocate = () => {
            attempts += 1;

            if (
                relocateRoutebar()
                ||
                attempts >= 8
            ) {
                return;
            }

            window.setTimeout(
                tryRelocate,
                30
            );
        };

        window.requestAnimationFrame(
            tryRelocate
        );

        /*
         * Las dos rutas heredadas de Almacén se eliminan visualmente
         * también desde JS por si un CSS antiguo tiene mayor prioridad.
         */
        document
            .querySelectorAll(
                '.alm-routebar, .alm-module-route, .alm-module-nav'
            )
            .forEach(oldRoute => {
                oldRoute.hidden = true;

                oldRoute.setAttribute(
                    'aria-hidden',
                    'true'
                );

                oldRoute.style.setProperty(
                    'display',
                    'none',
                    'important'
                );
            });
    };

    if (
        document.readyState ===
        'loading'
    ) {
        document.addEventListener(
            'DOMContentLoaded',
            initialize,
            { once: true }
        );
    }
    else {
        initialize();
    }
})();


/* NSQ_GLOBAL_NAV_V4_4_UNIFICAR_RUTAS_JS */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    if (!root || root.dataset.v44Ready === '1') {
        return;
    }

    root.dataset.v44Ready = '1';

    const initialize = () => {
        if (!document.body) {
            return;
        }

        const rawPath = window.location.pathname || '/';
        const path = rawPath.replace(/\/+$/, '') || '/';

        const isMenuIndex = /^\/Menu\/Index$/i.test(path) || /^\/Menu$/i.test(path);
        const isMenuGroup = /^\/Menu\/Grupo(?:\/|$)/i.test(path);
        const suppressRoutebar = isMenuIndex || isMenuGroup;

        if (isMenuIndex) {
            document.body.classList.add('nsq-menu-index-page');
        }

        if (isMenuGroup) {
            document.body.classList.add('nsq-menu-group-page');
        }

        if (suppressRoutebar) {
            document.body.classList.add('nsq-routebar-suppressed');
        }

        const removeSuppressedRoutebar = () => {
            if (!suppressRoutebar) {
                return false;
            }

            let removed = false;

            document
                .querySelectorAll('.nsq-global-routebar')
                .forEach(route => {
                    route.remove();
                    removed = true;
                });

            if (isMenuGroup) {
                document
                    .querySelectorAll('.migapan')
                    .forEach(route => {
                        route.classList.add('nsq-legacy-route-hidden');
                        route.hidden = true;
                        route.setAttribute('aria-hidden', 'true');
                    });
            }

            return removed;
        };

        const normalizeText = value => (value || '')
            .toString()
            .replace(/\s+/g, ' ')
            .trim()
            .toLocaleLowerCase('es-MX');

        const isBackLabel = element => {
            const text = normalizeText(element.textContent);

            if (text !== 'regresar' && text !== 'volver') {
                return false;
            }

            const classText = normalizeText(element.className);
            const hasBackClass = /back|return|regresar|volver/.test(classText);

            const hasBackIcon = Boolean(
                element.querySelector(
                    '.fa-arrow-left, .fa-chevron-left, .fas.fa-arrow-left, .fa-solid.fa-arrow-left, .bi-arrow-left'
                )
            );

            return hasBackClass || hasBackIcon;
        };

        const isLegacyRouteContainer = nav => {
            if (!nav || nav.classList.contains('nsq-global-routebar')) {
                return false;
            }

            const classText = normalizeText(nav.className);
            const aria = normalizeText(nav.getAttribute('aria-label'));

            return (
                /route|breadcrumb|miga|ruta/.test(classText)
                || /route|breadcrumb|miga|ruta/.test(aria)
                || Boolean(nav.querySelector('ol'))
            );
        };

        const suppressLegacyNavigation = () => {
            const globalRoute = document.querySelector('.nsq-global-routebar');

            if (!globalRoute) {
                return false;
            }

            document.body.classList.add('nsq-global-route-active');

            document
                .querySelectorAll(
                    '.pln-routebar, .alm-routebar, .alm-module-route, .alm-module-nav, .migapan'
                )
                .forEach(route => {
                    if (route === globalRoute || route.contains(globalRoute)) {
                        return;
                    }

                    route.classList.add('nsq-legacy-route-hidden');
                    route.hidden = true;
                    route.setAttribute('aria-hidden', 'true');
                    route.style.setProperty('display', 'none', 'important');
                });

            document
                .querySelectorAll('a, button')
                .forEach(control => {
                    if (!isBackLabel(control)) {
                        return;
                    }

                    if (
                        control.closest('.nsq-global-routebar')
                        || control.closest('.top-navbar')
                        || control.closest('.modal')
                        || control.closest('.offcanvas')
                    ) {
                        return;
                    }

                    const nav = control.closest('nav');

                    if (nav && isLegacyRouteContainer(nav)) {
                        nav.classList.add('nsq-legacy-route-hidden');
                        nav.hidden = true;
                        nav.setAttribute('aria-hidden', 'true');
                        nav.style.setProperty('display', 'none', 'important');
                        return;
                    }

                    control.classList.add('nsq-legacy-back-hidden');
                    control.hidden = true;
                    control.setAttribute('aria-hidden', 'true');
                    control.style.setProperty('display', 'none', 'important');
                });

            return true;
        };

        /*
         * V4.2/V4.3 crean la ruta durante el mismo DOMContentLoaded.
         * Reintentamos solo durante unos milisegundos para no depender
         * del orden de registro entre scripts.
         */
        let attempts = 0;

        const reconcileNavigation = () => {
            attempts += 1;

            removeSuppressedRoutebar();

            if (!suppressRoutebar) {
                suppressLegacyNavigation();
            }

            if (attempts < 10) {
                window.setTimeout(reconcileNavigation, 35);
            }
        };

        reconcileNavigation();

        const observer = new MutationObserver(() => {
            if (suppressRoutebar) {
                removeSuppressedRoutebar();
            }
            else {
                suppressLegacyNavigation();
            }
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        window.setTimeout(() => observer.disconnect(), 2200);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize, { once: true });
    }
    else {
        initialize();
    }
})();

            return removed;
        };

        const normalizeText = value => (value || '')
            .toString()
            .replace(/\s+/g, ' ')
            .trim()
            .toLocaleLowerCase('es-MX');

        const isBackLabel = element => {
            const text = normalizeText(element.textContent);

            if (text !== 'regresar' && text !== 'volver') {
                return false;
            }

            const classText = normalizeText(element.className);
            const hasBackClass = /back|return|regresar|volver/.test(classText);

            const hasBackIcon = Boolean(
                element.querySelector(
                    '.fa-arrow-left, .fa-chevron-left, .fas.fa-arrow-left, .fa-solid.fa-arrow-left, .bi-arrow-left'
                )
            );

            return hasBackClass || hasBackIcon;
        };

        const isLegacyRouteContainer = nav => {
            if (!nav || nav.classList.contains('nsq-global-routebar')) {
                return false;
            }

            const classText = normalizeText(nav.className);
            const aria = normalizeText(nav.getAttribute('aria-label'));

            return (
                /route|breadcrumb|miga|ruta/.test(classText)
                || /route|breadcrumb|miga|ruta/.test(aria)
                || Boolean(nav.querySelector('ol'))
            );
        };

        const suppressLegacyNavigation = () => {
            const globalRoute = document.querySelector('.nsq-global-routebar');

            if (!globalRoute) {
                return false;
            }

            document.body.classList.add('nsq-global-route-active');

            document
                .querySelectorAll(
                    '.pln-routebar, .alm-routebar, .alm-module-route, .alm-module-nav, .migapan'
                )
                .forEach(route => {
                    if (route === globalRoute || route.contains(globalRoute)) {
                        return;
                    }

                    route.classList.add('nsq-legacy-route-hidden');
                    route.hidden = true;
                    route.setAttribute('aria-hidden', 'true');
                    route.style.setProperty('display', 'none', 'important');
                });

            document
                .querySelectorAll('a, button')
                .forEach(control => {
                    if (!isBackLabel(control)) {
                        return;
                    }

                    if (
                        control.closest('.nsq-global-routebar')
                        || control.closest('.top-navbar')
                        || control.closest('.modal')
                        || control.closest('.offcanvas')
                    ) {
                        return;
                    }

                    const nav = control.closest('nav');

                    if (nav && isLegacyRouteContainer(nav)) {
                        nav.classList.add('nsq-legacy-route-hidden');
                        nav.hidden = true;
                        nav.setAttribute('aria-hidden', 'true');
                        nav.style.setProperty('display', 'none', 'important');
                        return;
                    }

                    control.classList.add('nsq-legacy-back-hidden');
                    control.hidden = true;
                    control.setAttribute('aria-hidden', 'true');
                    control.style.setProperty('display', 'none', 'important');
                });

            return true;
        };

        /*
         * V4.2/V4.3 crean la ruta durante el mismo DOMContentLoaded.
         * Reintentamos solo durante unos milisegundos para no depender
         * del orden de registro entre scripts.
         */
        let attempts = 0;

        const reconcileNavigation = () => {
            attempts += 1;

            removeSuppressedRoutebar();

            if (!suppressRoutebar) {
                suppressLegacyNavigation();
            }

            if (attempts < 10) {
                window.setTimeout(reconcileNavigation, 35);
            }
        };

        reconcileNavigation();

        const observer = new MutationObserver(() => {
            if (suppressRoutebar) {
                removeSuppressedRoutebar();
            }
            else {
                suppressLegacyNavigation();
            }
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        window.setTimeout(() => observer.disconnect(), 2200);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize, { once: true });
    }
    else {
        initialize();
    }
})();

/* NSQ_GLOBAL_NAV_V4_8_2_DEDUP_ROUTES_AND_SPACING_JS */
(() => {
    const normalizeText = value =>
        (value || '')
            .replace(/\s+/g, ' ')
            .replace(/[←‹«]/g, '')
            .trim()
            .toLowerCase();

    const cleanPath =
        (window.location.pathname || '')
            .replace(/\/+$/, '');

    const isMenuRoot =
        /^\/Menu(?:\/Index)?$/i.test(
            cleanPath
        );

    const isMenuGroup =
        /^\/Menu\/Grupo(?:\/|$)/i.test(
            cleanPath
        );

    const isExcludedContainer = element =>
        Boolean(
            element.closest(
                '.modal, .offcanvas, .dropdown-menu, ' +
                '.nsq-global-routebar, .top-navbar'
            )
        );

    const hideElement = (
        element,
        className
    ) => {
        if (!element) {
            return;
        }

        element.classList.add(
            className
        );

        element.hidden = true;

        element.setAttribute(
            'aria-hidden',
            'true'
        );

        element.style.setProperty(
            'display',
            'none',
            'important'
        );
    };

    const restoreMenuGroupRoute = () => {
        if (!document.body) {
            return;
        }

        document.body.classList.remove(
            'nsq-route-canonical-v48'
        );

        document.body.classList.add(
            'nsq-menu-group-native-route-v48'
        );

        document
            .querySelectorAll(
                '.nsq-global-routebar'
            )
            .forEach(route => {
                route.remove();
            });

        document
            .querySelectorAll(
                '.migapan'
            )
            .forEach(route => {
                route.hidden = false;

                route.removeAttribute(
                    'hidden'
                );

                route.removeAttribute(
                    'aria-hidden'
                );

                route.classList.remove(
                    'nsq-legacy-route-hidden',
                    'nsq-legacy-route-hidden-v48'
                );

                route.style.setProperty(
                    'display',
                    'flex',
                    'important'
                );

                route.style.setProperty(
                    'visibility',
                    'visible',
                    'important'
                );

                route.style.setProperty(
                    'opacity',
                    '1',
                    'important'
                );

                route
                    .querySelectorAll(
                        '[hidden], .nsq-legacy-back-hidden, ' +
                        '.nsq-legacy-back-hidden-v48'
                    )
                    .forEach(control => {
                        control.hidden = false;

                        control.removeAttribute(
                            'hidden'
                        );

                        control.removeAttribute(
                            'aria-hidden'
                        );

                        control.classList.remove(
                            'nsq-legacy-back-hidden',
                            'nsq-legacy-back-hidden-v48'
                        );

                        control.style.removeProperty(
                            'display'
                        );
                    });
            });
    };

    const routeLooksLegacy = element => {
        if (!element) {
            return false;
        }

        if (
            element.matches(
                '.pln-routebar, ' +
                '.alm-routebar, ' +
                '.alm-module-route, ' +
                '.alm-module-nav, ' +
                '.erp-page-breadcrumb'
            )
        ) {
            return true;
        }

        const text =
            normalizeText(
                element.textContent
            );

        const hasRouteWords =
            text.includes('inicio')
            &&
            (
                text.includes('regresar')
                ||
                text.includes('volver')
                ||
                element.querySelector(
                    'a[href="/"], ' +
                    'a[href="/Menu/Index"], ' +
                    'a[href="/menu/index"]'
                )
            );

        return Boolean(
            hasRouteWords
        );
    };

    const dedupeLegacyRoutes = routebar => {
        const main =
            routebar.closest(
                'main'
            )
            ||
            document.querySelector(
                'main'
            );

        if (!main) {
            return;
        }

        const explicitSelectors = [
            '.pln-routebar',
            '.alm-routebar',
            '.alm-module-route',
            '.alm-module-nav',
            '.erp-page-breadcrumb',
            '.page-breadcrumb',
            '.module-breadcrumb',
            '.breadcrumb-wrapper',
            '.breadcrumbs',
            'nav.breadcrumb',
            '.migapan'
        ];

        main
            .querySelectorAll(
                explicitSelectors.join(',')
            )
            .forEach(element => {
                if (
                    element === routebar
                    ||
                    routebar.contains(element)
                    ||
                    isExcludedContainer(element)
                ) {
                    return;
                }

                if (
                    element.classList.contains(
                        'migapan'
                    )
                    &&
                    isMenuGroup
                ) {
                    return;
                }

                if (routeLooksLegacy(element)) {
                    hideElement(
                        element,
                        'nsq-legacy-route-hidden-v48'
                    );
                }
            });

        /*
         * Elimina botones de retorno históricos aunque no vivan dentro
         * de un breadcrumb. Se limita a controles cuyo propósito es
         * inequívocamente Inicio / Regresar / Volver.
         */
        main
            .querySelectorAll(
                'a, button'
            )
            .forEach(control => {
                if (
                    routebar.contains(control)
                    ||
                    isExcludedContainer(control)
                ) {
                    return;
                }

                const text =
                    normalizeText(
                        control.textContent
                    );

                const rawHref =
                    (
                        control.getAttribute(
                            'href'
                        )
                        ||
                        ''
                    ).trim();

                const homeHref =
                    rawHref === '/'
                    ||
                    /^\/Menu\/Index\/?$/i.test(
                        rawHref
                    );

                const isBackText =
                    text === 'regresar'
                    ||
                    text === 'volver'
                    ||
                    (
                        text === 'inicio'
                        &&
                        homeHref
                    );

                if (!isBackText) {
                    return;
                }

                hideElement(
                    control,
                    'nsq-legacy-back-hidden-v48'
                );
            });
    };

    const compactTopSpace = routebar => {
        const host =
            routebar.parentElement;

        if (!host) {
            return;
        }

        let next =
            routebar.nextElementSibling;

        while (
            next
            &&
            (
                next.hidden
                ||
                window
                    .getComputedStyle(next)
                    .display === 'none'
            )
        ) {
            next =
                next.nextElementSibling;
        }

        if (!next) {
            return;
        }

        next.classList.add(
            'nsq-route-content-first-v48'
        );

        const classes =
            Array.from(
                next.classList
            );

        const isLayoutWrapper =
            next.classList.contains(
                'page'
            )
            ||
            next.classList.contains(
                'container'
            )
            ||
            next.classList.contains(
                'container-fluid'
            )
            ||
            classes.some(name =>
                /-page$/i.test(name)
            );

        next.style.setProperty(
            'margin-top',
            '0',
            'important'
        );

        if (isLayoutWrapper) {
            const computed =
                window.getComputedStyle(
                    next
                );

            const paddingTop =
                parseFloat(
                    computed.paddingTop
                )
                ||
                0;

            if (paddingTop > 10) {
                next.style.setProperty(
                    'padding-top',
                    '6px',
                    'important'
                );
            }
        }
    };

    const applyCanonicalRoute = () => {
        if (!document.body) {
            return false;
        }

        if (isMenuRoot) {
            return true;
        }

        if (isMenuGroup) {
            restoreMenuGroupRoute();
            return true;
        }

        const routebar =
            document.querySelector(
                '.nsq-global-routebar'
            );

        if (!routebar) {
            return false;
        }

        document.body.classList.remove(
            'nsq-menu-group-native-route-v48'
        );

        document.body.classList.add(
            'nsq-route-canonical-v48'
        );

        dedupeLegacyRoutes(
            routebar
        );

        compactTopSpace(
            routebar
        );

        return true;
    };

    const start = () => {
        let attempt = 0;

        const run = () => {
            attempt += 1;

            const complete =
                applyCanonicalRoute();

            if (
                complete
                ||
                attempt >= 6
            ) {
                return;
            }

            window.setTimeout(
                run,
                attempt * 45
            );
        };

        window.requestAnimationFrame(
            run
        );
    };

    if (
        document.readyState ===
        'loading'
    ) {
        document.addEventListener(
            'DOMContentLoaded',
            start,
            { once: true }
        );
    }
    else {
        start();
    }
})();

/* NSQ_NAVBAR_TITULOS_NARANJA_QUELL_V1_JS_START */
(() => {
    const root = document.querySelector('[data-nsq-department-nav]');
    const brandTitle = document.querySelector(
        '.top-navbar .nsq-brand-title, .top-navbar .navbar-title');

    if (!root || !brandTitle) return;

    const department = (root.dataset.activeDepartment || '').trim();

    brandTitle.classList.toggle(
        'nsq-navbar-department-title-quell',
        department.length > 0
    );
})();
/* NSQ_NAVBAR_TITULOS_NARANJA_QUELL_V1_JS_END */
