(function () {
    'use strict';

    var cfg = window.AppConfig || {};

    function resolve(template) {
        return String(template)
            .replace(/\{reunionWebBase\}/g, cfg.reunionWebBase || '')
            .replace(/\{authBase\}/g, cfg.authBase || '');
    }

    function apply(root) {
        root.querySelectorAll('[data-cfg-href]').forEach(function (el) {
            el.setAttribute('href', resolve(el.getAttribute('data-cfg-href')));
        });
        root.querySelectorAll('[data-cfg-src]').forEach(function (el) {
            el.setAttribute('src', resolve(el.getAttribute('data-cfg-src')));
        });
    }

    apply(document);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { apply(document); });
    }
})();
