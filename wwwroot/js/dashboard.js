/* =====================================================
   ARCHIVO: wwwroot/js/dashboard.js
   PROPÓSITO: Funcionalidades del dashboard principal
   ===================================================== */

class DashboardManager {
    constructor() {
        this.userDropdown = null;
        this.userToggle = null;
        this.actionCards = [];
        this.animationDelay = 150;

        this.init();
    }

    init() {
        // Inicializar cuando el DOM esté listo
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', () => this.setupComponents());
        } else {
            this.setupComponents();
        }
    }

    setupComponents() {
        this.setupUserMenu();
        this.setupActionCards();
        this.setupAnimations();
        this.setupAccessibility();

        console.log('🎯 Dashboard Manager inicializado correctamente');
    }

    /* ===== USER MENU MANAGEMENT ===== */
    setupUserMenu() {
        this.userToggle = document.getElementById('userToggle');
        this.userDropdown = document.getElementById('userDropdown');

        if (!this.userToggle || !this.userDropdown) {
            console.warn('⚠️ Elementos de menú de usuario no encontrados');
            return;
        }

        // Toggle dropdown al hacer click
        this.userToggle.addEventListener('click', (e) => {
            e.stopPropagation();
            this.toggleUserDropdown();
        });

        // Cerrar dropdown al hacer click fuera
        document.addEventListener('click', (e) => {
            if (!this.userToggle.contains(e.target) && !this.userDropdown.contains(e.target)) {
                this.closeUserDropdown();
            }
        });

        // Cerrar con ESC
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.userDropdown.classList.contains('show')) {
                this.closeUserDropdown();
                this.userToggle.focus();
            }
        });
    }

    toggleUserDropdown() {
        const isOpen = this.userDropdown.classList.contains('show');

        if (isOpen) {
            this.closeUserDropdown();
        } else {
            this.openUserDropdown();
        }
    }

    openUserDropdown() {
        this.userDropdown.classList.add('show');
        this.userToggle.setAttribute('aria-expanded', 'true');

        // Focus en el primer elemento del dropdown
        const firstItem = this.userDropdown.querySelector('.dropdown-item');
        if (firstItem) {
            setTimeout(() => firstItem.focus(), 100);
        }
    }

    closeUserDropdown() {
        this.userDropdown.classList.remove('show');
        this.userToggle.setAttribute('aria-expanded', 'false');
    }

    /* ===== ACTION CARDS MANAGEMENT ===== */
    setupActionCards() {
        this.actionCards = document.querySelectorAll('.action-card');

        // Setup click handlers para navegación
        this.actionCards.forEach(card => {
            // Enlaces internos (data-href)
            if (card.hasAttribute('data-href')) {
                card.addEventListener('click', (e) => {
                    e.preventDefault();
                    const url = card.getAttribute('data-href');
                    this.navigateToPage(url);
                });
            }

            // Enlaces externos (data-href-external)
            if (card.hasAttribute('data-href-external')) {
                card.addEventListener('click', (e) => {
                    e.preventDefault();
                    const url = card.getAttribute('data-href-external');
                    this.openExternalLink(url);
                });
            }

            // Agregar indicador de carga
            card.addEventListener('click', () => {
                this.addLoadingState(card);
            });
        });

        // Keyboard navigation
        this.setupCardKeyboardNavigation();
    }

    setupCardKeyboardNavigation() {
        this.actionCards.forEach(card => {
            card.setAttribute('tabindex', '0');
            card.setAttribute('role', 'button');

            card.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    card.click();
                }
            });
        });
    }

    navigateToPage(url) {
        console.log(`🔗 Navegando a: ${url}`);

        // Agregar transición suave
        document.body.style.opacity = '0.8';
        document.body.style.transition = 'opacity 0.3s ease';

        setTimeout(() => {
            window.location.href = url;
        }, 150);
    }

    openExternalLink(url) {
        console.log(`🔗 Abriendo enlace externo: ${url}`);
        window.open(url, '_blank', 'noopener,noreferrer');
    }

    addLoadingState(card) {
        card.classList.add('loading');
        card.style.pointerEvents = 'none';

        // Remover loading state después de un tiempo
        setTimeout(() => {
            card.classList.remove('loading');
            card.style.pointerEvents = '';
        }, 2000);
    }

    /* ===== ANIMATIONS ===== */
    setupAnimations() {
        // Animación de entrada para las cards
        this.animateCardsIn();

        // Setup Intersection Observer para animaciones al scroll
        this.setupScrollAnimations();
    }

    animateCardsIn() {
        this.actionCards.forEach((card, index) => {
            // Estado inicial
            card.style.opacity = '0';
            card.style.transform = 'translateY(20px)';
            card.classList.add('loading');

            // Animar con delay escalonado
            setTimeout(() => {
                card.style.transition = 'opacity 0.5s ease, transform 0.5s ease';
                card.style.opacity = '1';
                card.style.transform = 'translateY(0)';
                card.classList.remove('loading');
                card.classList.add('animate-in');
            }, index * this.animationDelay);
        });
    }

    setupScrollAnimations() {
        if ('IntersectionObserver' in window) {
            const observer = new IntersectionObserver((entries) => {
                entries.forEach(entry => {
                    if (entry.isIntersecting) {
                        entry.target.classList.add('animate-in');
                    }
                });
            }, {
                threshold: 0.1,
                rootMargin: '50px'
            });

            // Observar elementos que no están en viewport
            document.querySelectorAll('.welcome-section, .quick-actions').forEach(el => {
                observer.observe(el);
            });
        }
    }

    /* ===== ACCESSIBILITY ===== */
    setupAccessibility() {
        // Agregar aria-labels dinámicos
        this.actionCards.forEach(card => {
            const title = card.querySelector('.action-title')?.textContent;
            const description = card.querySelector('.action-description')?.textContent;

            if (title) {
                const ariaLabel = description
                    ? `${title}. ${description}`
                    : title;
                card.setAttribute('aria-label', ariaLabel);
            }
        });

        // Setup skip links
        this.setupSkipLinks();

        // Mejorar contraste en focus
        this.setupFocusStyles();
    }

    setupSkipLinks() {
        // Crear skip link si no existe
        if (!document.querySelector('.skip-link')) {
            const skipLink = document.createElement('a');
            skipLink.href = '#main-content';
            skipLink.className = 'skip-link visually-hidden';
            skipLink.textContent = 'Saltar al contenido principal';

            skipLink.addEventListener('focus', () => {
                skipLink.classList.remove('visually-hidden');
            });

            skipLink.addEventListener('blur', () => {
                skipLink.classList.add('visually-hidden');
            });

            document.body.insertBefore(skipLink, document.body.firstChild);
        }

        // Agregar ID al contenido principal si no existe
        const mainContent = document.querySelector('.main-content');
        if (mainContent && !mainContent.id) {
            mainContent.id = 'main-content';
        }
    }

    setupFocusStyles() {
        // Mejorar visibilidad del focus
        const style = document.createElement('style');
        style.textContent = `
            .action-card:focus {
                outline: 3px solid var(--color-primario);
                outline-offset: 2px;
                box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.3);
            }
            
            .user-toggle:focus {
                outline: 2px solid #fff;
                outline-offset: 2px;
            }
            
            .dropdown-item:focus {
                background: var(--gray-100);
                outline: 2px solid var(--color-primario);
                outline-offset: -2px;
            }
        `;
        document.head.appendChild(style);
    }

    /* ===== UTILITY METHODS ===== */
    showNotification(message, type = 'info', duration = 3000) {
        const notification = document.createElement('div');
        notification.className = `notification notification-${type}`;
        notification.textContent = message;

        notification.style.cssText = `
            position: fixed;
            top: 20px;
            right: 20px;
            background: var(--color-${type});
            color: white;
            padding: 1rem;
            border-radius: var(--border-radius-lg);
            box-shadow: var(--shadow-lg);
            z-index: var(--z-index-toast);
            transform: translateX(100%);
            transition: transform 0.3s ease;
        `;

        document.body.appendChild(notification);

        // Animar entrada
        requestAnimationFrame(() => {
            notification.style.transform = 'translateX(0)';
        });

        // Auto-remove
        setTimeout(() => {
            notification.style.transform = 'translateX(100%)';
            setTimeout(() => {
                if (notification.parentNode) {
                    notification.parentNode.removeChild(notification);
                }
            }, 300);
        }, duration);
    }

    /* ===== EVENT LISTENERS PARA INTERACCIONES EXTERNAS ===== */
    addCustomEventListeners() {
        // Custom events que pueden disparar otros módulos
        document.addEventListener('dashboard:refresh', () => {
            this.refreshDashboard();
        });

        document.addEventListener('dashboard:showNotification', (e) => {
            const { message, type, duration } = e.detail;
            this.showNotification(message, type, duration);
        });
    }

    refreshDashboard() {
        console.log('🔄 Refrescando dashboard...');

        // Re-animar cards
        this.actionCards.forEach(card => {
            card.classList.remove('animate-in');
        });

        setTimeout(() => {
            this.animateCardsIn();
        }, 100);
    }

    /* ===== PERFORMANCE MONITORING ===== */
    logPerformance() {
        if ('performance' in window) {
            window.addEventListener('load', () => {
                const loadTime = performance.now();
                console.log(`⚡ Dashboard cargado en ${loadTime.toFixed(2)}ms`);
            });
        }
    }
}

/* ===== UTILIDADES GLOBALES ===== */
window.DashboardUtils = {
    // Detectar empresa activa
    getCurrentCompany() {
        return document.body.getAttribute('data-empresa') || '1';
    },

    // Cambiar tema de empresa
    setCompanyTheme(empresaId) {
        document.body.setAttribute('data-empresa', empresaId);
        console.log(`🎨 Tema cambiado a empresa: ${empresaId}`);
    },

    // Verificar si usuario está autenticado
    isAuthenticated() {
        // Implementar según tu lógica de autenticación
        return sessionStorage.getItem('UsuarioID') !== null;
    },

    // Guardar preferencias del usuario
    saveUserPreference(key, value) {
        try {
            localStorage.setItem(`ns_pref_${key}`, JSON.stringify(value));
        } catch (e) {
            console.warn('No se pudo guardar preferencia:', e);
        }
    },

    // Cargar preferencias del usuario
    getUserPreference(key, defaultValue = null) {
        try {
            const value = localStorage.getItem(`ns_pref_${key}`);
            return value ? JSON.parse(value) : defaultValue;
        } catch (e) {
            console.warn('No se pudo cargar preferencia:', e);
            return defaultValue;
        }
    }
};

/* ===== INICIALIZACIÓN AUTOMÁTICA ===== */
// Crear instancia global del dashboard manager
window.dashboardManager = new DashboardManager();

// Exponer métodos útiles globalmente
window.showNotification = (message, type, duration) => {
    window.dashboardManager.showNotification(message, type, duration);
};

window.refreshDashboard = () => {
    window.dashboardManager.refreshDashboard();
};

/* ===== DEBUG HELPERS (solo en desarrollo) ===== */
if (window.location.hostname === 'localhost' || window.location.hostname.includes('dev')) {
    window.debugDashboard = {
        manager: window.dashboardManager,
        utils: window.DashboardUtils,

        testNotification() {
            window.showNotification('Notificación de prueba', 'success');
        },

        testAnimation() {
            window.refreshDashboard();
        },

        logState() {
            console.log('Dashboard State:', {
                cards: window.dashboardManager.actionCards.length,
                company: window.DashboardUtils.getCurrentCompany(),
                authenticated: window.DashboardUtils.isAuthenticated()
            });
        }
    };

    console.log('🔧 Debug helpers disponibles en window.debugDashboard');
}