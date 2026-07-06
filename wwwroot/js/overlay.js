(function () {
    const overlay = document.getElementById('app-overlay');
    if (!overlay) return;

    const show = () => overlay.classList.remove('is-hidden');
    const hide = () => overlay.classList.add('is-hidden');

    // Exponer por si quieres llamarlo manualmente
    window.AppOverlay = { show, hide };

    // Mostrar overlay al hacer clic en cards/enlaces que navegan
    document.addEventListener('click', function (e) {
        const trigger = e.target.closest('.action-card[data-href], [data-overlay-trigger], a[data-overlay-trigger]');
        if (!trigger) return;

        // Ignorar nuevas pestañas/descargas/modificadores
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;

        // Si es <a>, validar href
        if (trigger.tagName && trigger.tagName.toLowerCase() === 'a') {
            if (trigger.target === '_blank' || trigger.hasAttribute('download')) return;
            const href = trigger.getAttribute('href');
            if (!href || href.startsWith('#')) return;
        }

        show();
    }, true);

    // Si la página vuelve desde BFCache, ocultar overlay
    window.addEventListener('pageshow', (evt) => { if (evt.persisted) hide(); });
})();
