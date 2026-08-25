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
                    '<li><a class="dropdown-item" href="/">' +
                        '<i class="fa-solid fa-arrow-left me-1"></i>Retour au site' +
                    '</a></li>' +
                    '<li><hr class="dropdown-divider"></li>' +
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

    // ---- Reward targets ----

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

    // Mode toggle: which mini-form is shown for the next item to add.
    const formCard = document.getElementById('ao-form-card');
    const formUnique = document.getElementById('ao-form-unique');
    document.getElementById('ao-mode-card')?.addEventListener('change', () => {
        formCard.classList.replace('d-none', 'd-flex');
        formUnique.classList.replace('d-flex', 'd-none');
    });
    document.getElementById('ao-mode-unique')?.addEventListener('change', () => {
        formCard.classList.replace('d-flex', 'd-none');
        formUnique.classList.replace('d-none', 'd-flex');
    });

    // Booster type choices for the "Booster" mini-form.
    const boosterTypeSelect = document.getElementById('ao-booster-type');
    let boosterTypesByKey = new Map();
    const loadBoosterTypes = async () => {
        try {
            const res = await fetch('/api/admin/booster-types', { credentials: 'same-origin' });
            if (!res.ok) return;
            const types = await res.json();
            boosterTypesByKey = new Map(types.map((t) => [t.key, t.name]));
            boosterTypeSelect.innerHTML = types
                .map((t) => '<option value="' + escapeHtml(t.key) + '">' + escapeHtml(t.name) + '</option>')
                .join('');
        } catch { /* leave the select empty; adding a booster will just no-op */ }
    };

    // ---- Cumulative reward items (cards + boosters) before a single submit ----

    // { type: 'card', reference, quantity } | { type: 'booster', boosterTypeKey, quantity }
    const rewardItems = [];
    const rewardItemsEl = document.getElementById('ao-reward-items');
    const renderRewardItems = () => {
        rewardItemsEl.innerHTML = '';
        rewardItems.forEach((item, index) => {
            const label = item.type === 'card'
                ? item.reference + ' ×' + item.quantity
                : (boosterTypesByKey.get(item.boosterTypeKey) || item.boosterTypeKey) + ' ×' + item.quantity;
            const chip = document.createElement('span');
            chip.className = 'badge text-bg-light border d-inline-flex align-items-center gap-2 py-2 px-3';
            chip.textContent = label;
            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'btn-close';
            removeBtn.style.fontSize = '0.6rem';
            removeBtn.setAttribute('aria-label', 'Retirer');
            removeBtn.addEventListener('click', () => { rewardItems.splice(index, 1); renderRewardItems(); });
            chip.appendChild(removeBtn);
            rewardItemsEl.appendChild(chip);
        });
    };

    document.getElementById('ao-card-add-btn')?.addEventListener('click', () => {
        const reference = document.getElementById('ao-card-reference').value.trim();
        const quantity = parseInt(document.getElementById('ao-card-quantity').value, 10);
        if (!reference || !(quantity > 0)) { setStatus('error', 'Référence et quantité requises.'); return; }
        rewardItems.push({ type: 'card', reference, quantity });
        renderRewardItems();
        document.getElementById('ao-card-reference').value = '';
        document.getElementById('ao-card-quantity').value = '1';
    });

    document.getElementById('ao-booster-add-btn')?.addEventListener('click', () => {
        const boosterTypeKey = boosterTypeSelect.value;
        const quantity = parseInt(document.getElementById('ao-booster-quantity').value, 10);
        if (!boosterTypeKey || !(quantity > 0)) { setStatus('error', 'Type de booster et quantité requis.'); return; }
        rewardItems.push({ type: 'booster', boosterTypeKey, quantity });
        renderRewardItems();
        document.getElementById('ao-booster-quantity').value = '1';
    });

    // Status
    const statusEl = document.getElementById('ao-form-status');
    const setStatus = (kind, message) => {
        if (!kind) { statusEl.innerHTML = ''; return; }
        const cls = kind === 'success' ? 'alert-success' : kind === 'error' ? 'alert-danger' : 'alert-info';
        statusEl.innerHTML = '<div class="alert ' + cls + ' mb-0" role="alert">' + escapeHtml(message) + '</div>';
    };

    // All-or-nothing (see AdminEndpoints): a 204 means every target got the whole
    // reward in one event each, any error means nobody did.
    document.getElementById('ao-reward-form')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (targets.size === 0) { setStatus('error', 'Sélectionnez au moins un joueur.'); return; }
        if (rewardItems.length === 0) { setStatus('error', 'Ajoutez au moins une carte ou un booster.'); return; }

        setStatus('info', 'Envoi en cours…');
        try {
            const res = await fetch('/api/admin/rewards', {
                method: 'POST',
                credentials: 'same-origin',
                headers: jsonHeaders(),
                body: JSON.stringify({
                    keycloakUserIds: [...targets.keys()],
                    acquiredFrom: document.getElementById('ao-reward-acquired-from').value.trim(),
                    cards: rewardItems.filter((i) => i.type === 'card')
                        .map((i) => ({ cardReference: i.reference, quantity: i.quantity })),
                    boosters: rewardItems.filter((i) => i.type === 'booster')
                        .map((i) => ({ boosterTypeKey: i.boosterTypeKey, quantity: i.quantity })),
                }),
            });
            if (res.status === 204) {
                setStatus('success', 'Récompense distribuée à tous les joueurs ciblés.');
                rewardItems.length = 0;
                renderRewardItems();
            } else {
                setStatus('error', (await res.text()) || ('Erreur ' + res.status));
            }
        } catch (err) {
            setStatus('error', 'Erreur réseau : ' + (err?.message || err));
        }
    });

    // ---- Admins management ----

    const adminsBody = document.getElementById('ao-admins-body');
    const adminStatusEl = document.getElementById('ao-admin-status');
    const setAdminStatus = (kind, message) => {
        if (!kind) { adminStatusEl.innerHTML = ''; return; }
        const cls = kind === 'success' ? 'alert-success' : kind === 'error' ? 'alert-danger' : 'alert-info';
        adminStatusEl.innerHTML = '<div class="alert ' + cls + ' mb-0" role="alert">' + escapeHtml(message) + '</div>';
    };

    const loadAdmins = async () => {
        adminsBody.innerHTML = '<tr><td colspan="2" class="text-muted small">Chargement…</td></tr>';
        try {
            const res = await fetch('/api/admin/admins', { credentials: 'same-origin' });
            if (!res.ok) { adminsBody.innerHTML = '<tr><td colspan="2" class="text-danger small">Erreur de chargement.</td></tr>'; return; }

            const admins = await res.json();
            adminsBody.innerHTML = '';
            admins.forEach((a) => {
                const label = (a.pseudo || a.email || a.keycloakId) + (a.pseudo && a.email ? ' (' + a.email + ')' : '');
                const tr = document.createElement('tr');
                const labelTd = document.createElement('td');
                labelTd.textContent = label;
                const actionTd = document.createElement('td');
                const removeBtn = document.createElement('button');
                removeBtn.type = 'button';
                removeBtn.className = 'btn btn-sm btn-outline-danger';
                removeBtn.textContent = 'Retirer';
                removeBtn.addEventListener('click', () => demoteAdmin(a.keycloakId, label));
                actionTd.appendChild(removeBtn);
                tr.appendChild(labelTd);
                tr.appendChild(actionTd);
                adminsBody.appendChild(tr);
            });
        } catch {
            adminsBody.innerHTML = '<tr><td colspan="2" class="text-danger small">Erreur de chargement.</td></tr>';
        }
    };

    const setRole = async (keycloakId, role) => {
        const res = await fetch('/api/admin/users/' + encodeURIComponent(keycloakId) + '/role', {
            method: 'POST',
            credentials: 'same-origin',
            headers: jsonHeaders(),
            body: JSON.stringify({ role }),
        });
        if (!res.ok) throw new Error((await res.text()) || ('Erreur ' + res.status));
    };

    const demoteAdmin = async (keycloakId, label) => {
        if (!window.confirm('Retirer les droits admin de ' + label + ' ?')) return;
        setAdminStatus('info', 'Mise à jour…');
        try {
            await setRole(keycloakId, 'Player');
            setAdminStatus('success', label + ' n\'est plus admin.');
            await loadAdmins();
        } catch (err) {
            setAdminStatus('error', err.message || String(err));
        }
    };

    const promoteAdmin = async (keycloakId, label) => {
        if (!window.confirm('Donner les droits admin à ' + label + ' ?')) return;
        setAdminStatus('info', 'Mise à jour…');
        try {
            await setRole(keycloakId, 'Admin');
            setAdminStatus('success', label + ' est maintenant admin.');
            await loadAdmins();
        } catch (err) {
            setAdminStatus('error', err.message || String(err));
        }
    };

    const adminSearchInput = document.getElementById('ao-admin-search-input');
    const adminSearchBtn = document.getElementById('ao-admin-search-btn');
    const adminResultsEl = document.getElementById('ao-admin-search-results');
    const runAdminSearch = async () => {
        const term = adminSearchInput.value.trim();
        if (!term) { adminResultsEl.innerHTML = ''; return; }

        adminResultsEl.innerHTML = '<div class="text-muted small p-2">Recherche…</div>';
        try {
            const res = await fetch('/api/admin/users/search?term=' + encodeURIComponent(term), { credentials: 'same-origin' });
            if (!res.ok) { adminResultsEl.innerHTML = '<div class="text-danger small p-2">Erreur de recherche.</div>'; return; }

            const users = await res.json();
            if (!users.length) { adminResultsEl.innerHTML = '<div class="text-muted small p-2">Aucun résultat.</div>'; return; }

            adminResultsEl.innerHTML = '';
            users.forEach((u) => {
                const label = (u.pseudo || u.email || u.keycloakId) + (u.pseudo && u.email ? ' (' + u.email + ')' : '');
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'list-group-item list-group-item-action';
                item.textContent = label;
                item.addEventListener('click', () => promoteAdmin(u.keycloakId, label));
                adminResultsEl.appendChild(item);
            });
        } catch {
            adminResultsEl.innerHTML = '<div class="text-danger small p-2">Erreur de recherche.</div>';
        }
    };
    adminSearchBtn?.addEventListener('click', runAdminSearch);
    adminSearchInput?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); runAdminSearch(); }
    });

    document.getElementById('ao-admin-manual-add-btn')?.addEventListener('click', () => {
        const input = document.getElementById('ao-admin-manual-id');
        const id = input.value.trim();
        if (!id) return;
        promoteAdmin(id, id);
        input.value = '';
    });

    // ---- Bootstrap: must be logged in AND pass the admin-only ping check ----
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
            await loadBoosterTypes();
            await loadAdmins();
        } catch {
            deniedBlock.hidden = false;
        }
    })();
})();
