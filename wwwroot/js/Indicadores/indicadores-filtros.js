(() => {
    const roots = document.querySelectorAll('[data-period-filter]');

    roots.forEach(root => {
        const buttons = Array.from(root.querySelectorAll('[data-period-mode]'));
        const panels = Array.from(root.querySelectorAll('[data-period-panel]'));
        const hidden = root.querySelector('[data-period-value]');

        const activate = mode => {
            if (!hidden) return;
            hidden.value = mode;

            buttons.forEach(button => {
                button.classList.toggle('active', button.dataset.periodMode === mode);
            });

            panels.forEach(panel => {
                panel.classList.toggle('is-hidden', panel.dataset.periodPanel !== mode);
            });

            const activePanel = panels.find(panel => panel.dataset.periodPanel === mode);
            const input = activePanel?.querySelector('input');
            if (input) input.focus({ preventScroll: true });
        };

        buttons.forEach(button => {
            button.addEventListener('click', () => activate(button.dataset.periodMode || 'semana'));
        });
    });
})();
