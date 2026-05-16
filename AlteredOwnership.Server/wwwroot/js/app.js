(() => {
    const html = document.documentElement;
    const THEME_KEY = 'ar_theme';
    const LANG_KEY = 'ar_lang';
    const LANG_FLAGS = { en: '🇬🇧', fr: '🇫🇷', es: '🇪🇸', it: '🇮🇹', de: '🇩🇪' };
    const DEFAULT_LANG = 'en';
    const DEFAULT_THEME = 'light';

    const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));

    // i18n
    let currentLang = DEFAULT_LANG;
    const t = (key) => {
        const dict = window.AO_I18N || {};
        return (dict[currentLang] && dict[currentLang][key])
            || (dict[DEFAULT_LANG] && dict[DEFAULT_LANG][key])
            || key;
    };
    const applyI18n = () => {
        document.querySelectorAll('[data-i18n]').forEach((el) => {
            el.textContent = t(el.dataset.i18n);
        });
        document.querySelectorAll('[data-i18n-html]').forEach((el) => {
            el.innerHTML = t(el.dataset.i18nHtml);
        });
        document.querySelectorAll('[data-i18n-title]').forEach((el) => {
            el.title = t(el.dataset.i18nTitle);
        });
        document.querySelectorAll('[data-i18n-aria-label]').forEach((el) => {
            el.setAttribute('aria-label', t(el.dataset.i18nAriaLabel));
        });
    };

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
        currentLang = LANG_FLAGS[lang] ? lang : DEFAULT_LANG;
        html.lang = currentLang;
        const flag = document.querySelector('[data-lang-current]');
        if (flag) flag.textContent = LANG_FLAGS[currentLang];
        document.querySelectorAll('[data-lang]').forEach((a) => {
            a.classList.toggle('active', a.dataset.lang === currentLang);
        });
        applyI18n();
        // Auth control is built in JS, so it needs to be re-rendered after language switch.
        if (currentAuth === 'anonymous') renderLogin();
        else if (currentAuth) renderUser(currentAuth);
    };
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
    const importAnonBlock = document.getElementById('ao-import-anon');
    const importAuthBlock = document.getElementById('ao-import-auth');
    const SILENT_LOGIN_KEY = 'ao_silent_login_tried';
    // null = unknown yet, 'anonymous' = login button, object = signed-in user.
    let currentAuth = null;
    const renderLogin = () => {
        currentAuth = 'anonymous';
        if (importAnonBlock) importAnonBlock.hidden = false;
        if (importAuthBlock) importAuthBlock.hidden = true;
        authControl.innerHTML =
            '<a href="/api/auth/login?returnUrl=/" class="btn-login">' +
            '<i class="fa-solid fa-user me-1"></i><span>' + escapeHtml(t('auth.login')) + '</span></a>';
    };
    const renderUser = (me) => {
        currentAuth = me;
        if (importAnonBlock) importAnonBlock.hidden = true;
        if (importAuthBlock) importAuthBlock.hidden = false;
        sessionStorage.removeItem(SILENT_LOGIN_KEY);
        const name = me.pseudo || me.email || me.sub;
        const email = me.email || '';
        authControl.innerHTML =
            '<div class="dropdown">' +
                '<button class="btn-user-badge dropdown-toggle" type="button" data-bs-toggle="dropdown" aria-expanded="false">' +
                    '<i class="fa-solid fa-user me-1"></i><span>' + escapeHtml(name) + '</span>' +
                '</button>' +
                '<ul class="dropdown-menu dropdown-menu-end">' +
                    (email ? '<li><span class="dropdown-item-text small text-muted">' + escapeHtml(email) + '</span></li>' : '') +
                    '<li><a class="dropdown-item" href="' + (window.AppConfig && window.AppConfig.reunionWebBase || '') + '/pages/account">' +
                        '<i class="fa-solid fa-user me-1"></i>' + escapeHtml(t('auth.account')) +
                    '</a></li>' +
                    '<li><hr class="dropdown-divider"></li>' +
                    '<li>' +
                        '<form method="POST" action="/api/auth/logout" style="margin:0">' +
                            '<button type="submit" class="dropdown-item text-danger">' +
                                '<i class="fa-solid fa-right-from-bracket me-1"></i>' + escapeHtml(t('auth.logout')) +
                            '</button>' +
                        '</form>' +
                    '</li>' +
                '</ul>' +
            '</div>';
    };

    // On 401, try a single silent OIDC login per browser session: if the user already
    // has a Keycloak SSO session we get logged in transparently; otherwise Keycloak
    // returns login_required and we come back here to render the login button.
    const tryAutoLogin = () => {
        if (sessionStorage.getItem(SILENT_LOGIN_KEY) === '1') {
            renderLogin();
            return;
        }
        sessionStorage.setItem(SILENT_LOGIN_KEY, '1');
        const returnUrl = window.location.pathname + window.location.search;
        window.location.replace('/api/auth/login?silent=true&returnUrl=' + encodeURIComponent(returnUrl));
    };

    // Apply language now that renderLogin/renderUser exist (re-render hook needs them).
    applyLang(localStorage.getItem(LANG_KEY) || DEFAULT_LANG);

    (async () => {
        try {
            const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
            if (!res.ok) { tryAutoLogin(); return; }
            renderUser(await res.json());
        } catch {
            renderLogin();
        }
    })();

    // Collection import
    const importForm = document.getElementById('ao-import-form');
    const importFile = document.getElementById('ao-import-file');
    const importTerms = document.getElementById('ao-import-terms');
    const importSubmit = document.getElementById('ao-import-submit');
    const importStatus = document.getElementById('ao-import-status');
    const setStatus = (kind, message) => {
        if (!importStatus) return;
        if (!kind) { importStatus.innerHTML = ''; return; }
        const cls = kind === 'success' ? 'alert-success' : kind === 'error' ? 'alert-danger' : 'alert-info';
        importStatus.innerHTML = '<div class="alert ' + cls + ' mb-0" role="alert">' + escapeHtml(message) + '</div>';
    };
    const refreshImportSubmit = () => {
        if (!importSubmit) return;
        importSubmit.disabled = !(importFile?.files?.[0] && importTerms?.checked);
    };
    importFile?.addEventListener('change', refreshImportSubmit);
    importTerms?.addEventListener('change', refreshImportSubmit);
    importForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const file = importFile?.files?.[0];
        if (!file || !importTerms?.checked) return;

        const body = new FormData();
        body.append('file', file);
        body.append('termsAccepted', 'true');

        importSubmit.disabled = true;
        setStatus('info', t('import.inProgress'));
        try {
            const res = await fetch('/api/collection/import', {
                method: 'POST',
                credentials: 'same-origin',
                body,
            });
            if (res.status === 204) {
                setStatus('success', t('import.success'));
                importForm.reset();
            } else if (res.status === 401) {
                setStatus('error', t('import.sessionExpired'));
            } else {
                const text = (await res.text()) || (t('import.error') + ' ' + res.status);
                setStatus('error', text);
            }
        } catch (err) {
            setStatus('error', t('import.sendError') + ' : ' + (err?.message || err));
        } finally {
            refreshImportSubmit();
        }
    });
})();
