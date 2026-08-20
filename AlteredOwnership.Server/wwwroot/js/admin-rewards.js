(() => {
    const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));

    const authControl = document.getElementById('ao-auth-control');
    const deniedBlock = document.getElementById('ao-admin-denied');
    const contentBlock = document.getElementById('ao-admin-content');

    // Antiforgery request token for the current session, fetched once signed in.
    let csrfToken = null;
    const fetchCsrfToken = async () => {
        try {
            const res = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
            if (res.ok) csrfToken = (await res.json()).token;
        } catch { /* leave null; protected calls will surface the error */ }
    };
    const jsonHeaders = () => Object.assign(
        { 'Content-Type': 'application/json' },
        csrfToken ? { 'X-CSRF-TOKEN': csrfToken } : {});

    const renderLogin = () => {
        const loginHref = '/api/auth/login?returnUrl=' + encodeURIComponent(window.location.pathname);
        authControl.innerHTML =
            '<a href="' + loginHref + '" class="btn btn-sm btn-primary">' +
            '<i class="fa-solid fa-user me-1"></i>Se connecter</a>';
    };
    const renderUser = (me) => {
        const name = me.pseudo || me.email || me.sub;
        authControl.innerHTML =
            '<div class="dropdown">' +
                '<button class="btn btn-sm btn-outline-secondary dropdown-toggle" type="button" data-bs-toggle="dropdown" aria-expanded="false">' +
                    '<i class="fa-solid fa-user me-1"></i><span>' + escapeHtml(name) + '</span>' +
                '</button>' +
                '<ul class="dropdown-menu dropdown-menu-end">' +
                    '<li>' +
                        '<form method="POST" action="/api/auth/logout" style="margin:0">' +
                            (csrfToken ? '<input type="hidden" name="__RequestVerificationToken" value="' + escapeHtml(csrfToken) + '">' : '') +
                            '<button type="submit" class="dropdown-item text-danger">' +
                                '<i class="fa-solid fa-right-from-bracket me-1"></i>Se déconnecter' +
                            '</button>' +
                        '</form>' +
                    '</li>' +
                '</ul>' +
            '</div>';
    };

    // Selected targets: keycloakId -> display label.
    const targets = new Map();
    const targetsEl = document.getElementById('ao-targets');
    const renderTargets = () => {
        targetsEl.innerHTML = '';
        targets.forEach((label, id) => {
            const chip = document.createElement('span');
            chip.className = 'badge text-bg-light border d-inline-flex align-items-center gap-2 py-2 px-3';
            chip.textContent = label;
            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'btn-close';
            removeBtn.style.fontSize = '0.6rem';
            removeBtn.setAttribute('aria-label', 'Retirer');
            removeBtn.addEventListener('click', () => { targets.delete(id); renderTargets(); });
            chip.appendChild(removeBtn);
            targetsEl.appendChild(chip);
        });
    };
    const addTarget = (id, label) => {
        const trimmed = (id || '').trim();
        if (!trimmed) return;
        targets.set(trimmed, label || trimmed);
        renderTargets();
    };

    // Player search
    const searchInput = document.getElementById('ao-search-input');
    const searchBtn = document.getElementById('ao-search-btn');
    const resultsEl = document.getElementById('ao-search-results');
    const runSearch = async () => {
        const term = searchInput.value.trim();
        if (!term) { resultsEl.innerHTML = ''; return; }

        resultsEl.innerHTML = '<div class="text-muted small p-2">Recherche…</div>';
        try {
            const res = await fetch('/api/admin/users/search?term=' + encodeURIComponent(term), { credentials: 'same-origin' });
            if (!res.ok) { resultsEl.innerHTML = '<div class="text-danger small p-2">Erreur de recherche.</div>'; return; }

            const users = await res.json();
            if (!users.length) { resultsEl.innerHTML = '<div class="text-muted small p-2">Aucun résultat.</div>'; return; }

            resultsEl.innerHTML = '';
            users.forEach((u) => {
                const label = (u.pseudo || u.email || u.keycloakId) + (u.pseudo && u.email ? ' (' + u.email + ')' : '');
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'list-group-item list-group-item-action';
                item.textContent = label;
                item.addEventListener('click', () => addTarget(u.keycloakId, u.pseudo || u.email || u.keycloakId));
                resultsEl.appendChild(item);
            });
        } catch {
            resultsEl.innerHTML = '<div class="text-danger small p-2">Erreur de recherche.</div>';
        }
    };
    searchBtn?.addEventListener('click', runSearch);
    searchInput?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); runSearch(); }
    });

    // Manually pasted Keycloak ids, one per line.
    document.getElementById('ao-manual-add-btn')?.addEventListener('click', () => {
        const textarea = document.getElementById('ao-manual-ids');
        textarea.value.split('\n').map((l) => l.trim()).filter(Boolean).forEach((id) => addTarget(id, id));
        textarea.value = '';
    });

    // Mode toggle
    const formCard = document.getElementById('ao-form-card');
    const formUnique = document.getElementById('ao-form-unique');
    document.getElementById('ao-mode-card')?.addEventListener('change', () => {
        formCard.hidden = false;
        formUnique.hidden = true;
    });
    document.getElementById('ao-mode-unique')?.addEventListener('change', () => {
        formCard.hidden = true;
        formUnique.hidden = false;
    });

    // Status + results
    const statusEl = document.getElementById('ao-form-status');
    const setStatus = (kind, message) => {
        if (!kind) { statusEl.innerHTML = ''; return; }
        const cls = kind === 'success' ? 'alert-success' : kind === 'error' ? 'alert-danger' : 'alert-info';
        statusEl.innerHTML = '<div class="alert ' + cls + ' mb-0" role="alert">' + escapeHtml(message) + '</div>';
    };

    const resultsSection = document.getElementById('ao-results-section');
    const resultsBody = document.getElementById('ao-results-body');
    const renderResults = (results) => {
        resultsBody.innerHTML = '';
        results.forEach((r) => {
            const detail = r.success
                ? (r.grantedReferences && r.grantedReferences.length ? r.grantedReferences.join(', ') : 'OK')
                : (r.error || 'Erreur');
            const tr = document.createElement('tr');
            tr.innerHTML =
                '<td>' + escapeHtml(r.keycloakUserId) + '</td>' +
                '<td>' + (r.success ? '<span class="text-success">Succès</span>' : '<span class="text-danger">Échec</span>') + '</td>' +
                '<td>' + escapeHtml(detail) + '</td>';
            resultsBody.appendChild(tr);
        });
        resultsSection.hidden = false;
    };

    const submitReward = async (path, body) => {
        if (targets.size === 0) { setStatus('error', 'Sélectionnez au moins un joueur.'); return; }

        setStatus('info', 'Envoi en cours…');
        try {
            const res = await fetch(path, {
                method: 'POST',
                credentials: 'same-origin',
                headers: jsonHeaders(),
                body: JSON.stringify(Object.assign({ keycloakUserIds: [...targets.keys()] }, body)),
            });
            if (res.ok) {
                setStatus(null, '');
                renderResults(await res.json());
            } else {
                setStatus('error', (await res.text()) || ('Erreur ' + res.status));
            }
        } catch (err) {
            setStatus('error', 'Erreur réseau : ' + (err?.message || err));
        }
    };

    formCard?.addEventListener('submit', (e) => {
        e.preventDefault();
        submitReward('/api/admin/rewards/card', {
            cardReference: document.getElementById('ao-card-reference').value.trim(),
            quantity: parseInt(document.getElementById('ao-card-quantity').value, 10),
            acquiredFrom: document.getElementById('ao-card-acquired-from').value.trim(),
        });
    });

    formUnique?.addEventListener('submit', (e) => {
        e.preventDefault();
        const set = document.getElementById('ao-unique-set').value.trim();
        submitReward('/api/admin/rewards/random-unique', {
            set: set || null,
            quantity: parseInt(document.getElementById('ao-unique-quantity').value, 10),
            acquiredFrom: document.getElementById('ao-unique-acquired-from').value.trim(),
        });
    });

    // Bootstrap: must be logged in AND pass the admin-only ping check.
    (async () => {
        try {
            const meRes = await fetch('/api/auth/me', { credentials: 'same-origin' });
            if (!meRes.ok) { renderLogin(); deniedBlock.hidden = false; return; }

            const me = await meRes.json();
            await fetchCsrfToken();
            renderUser(me);

            const pingRes = await fetch('/api/admin/ping', { credentials: 'same-origin' });
            if (!pingRes.ok) { deniedBlock.hidden = false; return; }

            contentBlock.hidden = false;
        } catch {
            deniedBlock.hidden = false;
        }
    })();
})();
