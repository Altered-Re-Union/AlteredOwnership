(() => {
    const html = document.documentElement;
    const SUPPORTED_LANGS = ['en', 'fr'];
    const DEFAULT_LANG = 'en';

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

    // Language is driven by the Keycloak `locale` claim; there is no on-site override.
    const applyLang = (lang) => {
        currentLang = SUPPORTED_LANGS.includes(lang) ? lang : DEFAULT_LANG;
        html.lang = currentLang;
        applyI18n();
        // Auth control is built in JS, so it needs to be re-rendered after a language change.
        if (currentAuth === 'anonymous') renderLogin();
        else if (currentAuth) renderUser(currentAuth);
    };
    // Maps a raw locale (e.g. "fr", "fr-FR", "de_DE") to a supported UI language, or null.
    const normalizeLang = (raw) => {
        if (!raw) return null;
        const base = String(raw).toLowerCase().split(/[-_]/)[0];
        return SUPPORTED_LANGS.includes(base) ? base : null;
    };

    // Auth
    const authControl = document.getElementById('ao-auth-control');
    const importAnonBlock = document.getElementById('ao-import-anon');
    const importAuthBlock = document.getElementById('ao-import-auth');
    const SILENT_LOGIN_KEY = 'ao_silent_login_tried';
    // null = unknown yet, 'anonymous' = login button, object = signed-in user.
    let currentAuth = null;
    // Antiforgery request token for the current session, fetched once we're signed in.
    let csrfToken = null;
    const fetchCsrfToken = async () => {
        try {
            const res = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
            if (res.ok) csrfToken = (await res.json()).token;
        } catch { /* leave null; protected calls will surface the error */ }
    };
    const renderLogin = () => {
        currentAuth = 'anonymous';
        if (importAnonBlock) importAnonBlock.hidden = false;
        if (importAuthBlock) importAuthBlock.hidden = true;
        authControl.innerHTML =
            '<a href="/api/auth/login?returnUrl=/" class="btn btn-sm btn-primary">' +
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
                '<button class="btn btn-sm btn-outline-secondary dropdown-toggle" type="button" data-bs-toggle="dropdown" aria-expanded="false">' +
                    '<i class="fa-solid fa-user me-1"></i><span>' + escapeHtml(name) + '</span>' +
                '</button>' +
                '<ul class="dropdown-menu dropdown-menu-end">' +
                    (email ? '<li><span class="dropdown-item-text small text-muted">' + escapeHtml(email) + '</span></li>' : '') +
                    '<li><a class="dropdown-item" href="' + (window.AppConfig && window.AppConfig.authBase || '') + '/realms/players/account/">' +
                        '<i class="fa-solid fa-id-card me-1"></i>' + escapeHtml(t('auth.editProfile')) +
                    '</a></li>' +
                    '<li><hr class="dropdown-divider"></li>' +
                    '<li>' +
                        '<form method="POST" action="/api/auth/logout" style="margin:0">' +
                            (csrfToken ? '<input type="hidden" name="__RequestVerificationToken" value="' + escapeHtml(csrfToken) + '">' : '') +
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

    // Render in English until /me resolves the Keycloak locale (renderLogin/renderUser
    // must exist first — the re-render hook needs them).
    applyLang(DEFAULT_LANG);

    (async () => {
        try {
            const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
            if (!res.ok) { tryAutoLogin(); return; }
            const me = await res.json();
            // Token is session-bound, so fetch it before rendering anything that uses it.
            await fetchCsrfToken();
            // Language comes from the Keycloak account locale; fall back to English.
            applyLang(normalizeLang(me.locale) || DEFAULT_LANG);
            renderUser(me);
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
                headers: csrfToken ? { 'X-CSRF-TOKEN': csrfToken } : {},
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
