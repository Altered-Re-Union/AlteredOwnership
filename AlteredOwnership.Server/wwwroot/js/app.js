(() => {
    const html = document.documentElement;
    const THEME_KEY = 'ar_theme';
    const LANG_KEY = 'ar_lang';
    const LANG_FLAGS = { en: '🇬🇧', fr: '🇫🇷', es: '🇪🇸', it: '🇮🇹', de: '🇩🇪' };
    const DEFAULT_LANG = 'fr';
    const DEFAULT_THEME = 'light';

    const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));

    // Theme
    const applyTheme = (theme) => {
        html.dataset.theme = theme;
        const icon = document.querySelector('#header-theme-toggle i');
        if (icon) icon.className = theme === 'dark' ? 'fa-solid fa-sun' : 'fa-solid fa-moon';
    };
    applyTheme(localStorage.getItem(THEME_KEY) || DEFAULT_THEME);
    document.getElementById('header-theme-toggle')?.addEventListener('click', () => {
        const next = html.dataset.theme === 'dark' ? 'light' : 'dark';
        localStorage.setItem(THEME_KEY, next);
        applyTheme(next);
    });

    // Language
    const applyLang = (lang) => {
        html.lang = lang;
        const flag = document.querySelector('[data-lang-current]');
        if (flag) flag.textContent = LANG_FLAGS[lang] || LANG_FLAGS[DEFAULT_LANG];
        document.querySelectorAll('[data-lang]').forEach((a) => {
            a.classList.toggle('active', a.dataset.lang === lang);
        });
    };
    applyLang(localStorage.getItem(LANG_KEY) || DEFAULT_LANG);
    document.querySelectorAll('[data-lang]').forEach((a) => {
        a.addEventListener('click', (e) => {
            e.preventDefault();
            const lang = a.dataset.lang;
            if (!LANG_FLAGS[lang]) return;
            localStorage.setItem(LANG_KEY, lang);
            applyLang(lang);
        });
    });

    // Auth
    const authControl = document.getElementById('ao-auth-control');
    const renderLogin = () => {
        authControl.innerHTML =
            '<a href="/api/auth/login?returnUrl=/" class="btn-login">' +
            '<i class="fa-solid fa-user me-1"></i><span>Connexion</span></a>';
    };
    const renderUser = (me) => {
        const name = me.pseudo || me.email || me.sub;
        const email = me.email || '';
        authControl.innerHTML =
            '<div class="dropdown">' +
                '<button class="btn-user-badge dropdown-toggle" type="button" data-bs-toggle="dropdown" aria-expanded="false">' +
                    '<i class="fa-solid fa-user me-1"></i><span>' + escapeHtml(name) + '</span>' +
                '</button>' +
                '<ul class="dropdown-menu dropdown-menu-end">' +
                    (email ? '<li><span class="dropdown-item-text small text-muted">' + escapeHtml(email) + '</span></li>' : '') +
                    '<li><a class="dropdown-item" href="https://www.beta.altered-reunion.com/pages/account">' +
                        '<i class="fa-solid fa-user me-1"></i>Mon compte' +
                    '</a></li>' +
                    '<li><hr class="dropdown-divider"></li>' +
                    '<li>' +
                        '<form method="POST" action="/api/auth/logout" style="margin:0">' +
                            '<button type="submit" class="dropdown-item text-danger">' +
                                '<i class="fa-solid fa-right-from-bracket me-1"></i>Déconnexion' +
                            '</button>' +
                        '</form>' +
                    '</li>' +
                '</ul>' +
            '</div>';
    };

    (async () => {
        try {
            const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
            if (!res.ok) { renderLogin(); return; }
            renderUser(await res.json());
        } catch {
            renderLogin();
        }
    })();
})();
