(() => {
    const apply = root => {
        const scope = root && root.querySelectorAll ? root : document;
        if (root && root.nodeType === Node.ELEMENT_NODE && root.hasAttribute('data-nsq-style')) {
            root.style.cssText += ';' + (root.getAttribute('data-nsq-style') || '');
            root.removeAttribute('data-nsq-style');
        }
        scope.querySelectorAll('[data-nsq-style]').forEach(element => {
            element.style.cssText += ';' + (element.getAttribute('data-nsq-style') || '');
            element.removeAttribute('data-nsq-style');
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => apply(document), { once: true });
    } else {
        apply(document);
    }

    const observer = new MutationObserver(mutations => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node.nodeType === Node.ELEMENT_NODE) apply(node);
            }
        }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
})();
