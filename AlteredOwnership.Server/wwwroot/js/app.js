(() => {
    // i18n engine (dictionary lookup, data-i18n rendering, language state, the flag
    // toggle) lives in i18n.js, shared with admin.js -- see that file for details.
    const { t } = window.AoI18n;

    const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));

    // Auth control is built in JS, so it needs to be re-rendered after a language change.
    window.AoI18n.onLangChange(() => {
        if (currentAuth === 'anonymous') renderLogin();
        else if (currentAuth) renderUser(currentAuth);
    });

    // Auth
    const authControl = document.getElementById('ao-auth-control');
    const importAnonBlock = document.getElementById('ao-import-anon');
    const importAuthBlock = document.getElementById('ao-import-auth');
    const SILENT_LOGIN_KEY = 'ao_silent_login_tried';
    // null = unknown yet, 'anonymous' = login button, object = signed-in user.
    let currentAuth = null;
    const currentReturnUrl = () => window.location.pathname + window.location.search;
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
        const loginHref = '/api/auth/login?returnUrl=' + encodeURIComponent(currentReturnUrl());
        if (importAnonBlock) {
            importAnonBlock.hidden = false;
            const importLoginLink = importAnonBlock.querySelector('a');
            if (importLoginLink) importLoginLink.href = loginHref;
        }
        if (importAuthBlock) importAuthBlock.hidden = true;
        authControl.innerHTML =
            '<a href="' + loginHref + '" class="btn btn-sm btn-primary">' +
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
                    (me.isAdmin
                        ? '<li><a class="dropdown-item" href="/admin/">' +
                            '<i class="fa-solid fa-user-shield me-1"></i>' + escapeHtml(t('auth.admin')) +
                        '</a></li>'
                        : '') +
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

    // "Back" link in the header: collection/boosters/history now live on the main
    // altered.re site (a PHP plugin), so this points there instead of a local page.
    const backLink = document.getElementById('ao-back-link');
    if (backLink && window.AppConfig && window.AppConfig.reunionWebBase) {
        backLink.href = window.AppConfig.reunionWebBase + '/pages/ownership';
    }

    // On 401, try a single silent OIDC login per browser session: if the user already
    // has a Keycloak SSO session we get logged in transparently; otherwise Keycloak
    // returns login_required and we come back here to render the login button.
    const tryAutoLogin = () => {
        if (sessionStorage.getItem(SILENT_LOGIN_KEY) === '1') {
            renderLogin();
            return;
        }
        sessionStorage.setItem(SILENT_LOGIN_KEY, '1');
        window.location.replace('/api/auth/login?silent=true&returnUrl=' + encodeURIComponent(currentReturnUrl()));
    };

    // Render with a stored flag choice if there is one, English otherwise, until /me
    // resolves the Keycloak locale (renderLogin/renderUser must exist first — the
    // re-render hook needs them).
    window.AoI18n.initLang();

    // Other page scripts (history.js, boosters.js) read document.documentElement.lang
    // for their own locale-dependent fetches/formatting. They run as separate deferred
    // scripts right after this one and would otherwise race the /me call below — firing
    // before the real locale lands and permanently reading "en" for that page load (stale
    // English dates/card names even for a French account). They await this instead.
    let resolveLangReady;
    window.AO_LANG_READY = new Promise((resolve) => { resolveLangReady = resolve; });

    (async () => {
        try {
            const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
            if (!res.ok) { tryAutoLogin(); return; }
            const me = await res.json();
            // Token is session-bound, so fetch it before rendering anything that uses it.
            await fetchCsrfToken();
            // Falls back to the Keycloak account locale, but a stored flag choice (set
            // by clicking the toggle) always wins over it -- see initLang in i18n.js.
            window.AoI18n.initLang(me.locale);
            renderUser(me);
        } catch {
            renderLogin();
        } finally {
            resolveLangReady();
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
