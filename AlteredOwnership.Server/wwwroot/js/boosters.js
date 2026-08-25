(() => {
    const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));

    const t = (key, fallback) => {
        const dict = window.AO_I18N || {};
        const lang = document.documentElement.lang || 'en';
        return (dict[lang] && dict[lang][key]) || (dict.en && dict.en[key]) || fallback;
    };

    const locale = () => document.documentElement.lang || 'en';

    const anonEl = document.getElementById('ao-boosters-anon');
    const loadingEl = document.getElementById('ao-boosters-loading');
    const emptyEl = document.getElementById('ao-boosters-empty');
    const errorEl = document.getElementById('ao-boosters-error');
    const gridEl = document.getElementById('ao-boosters-grid');

    const coverHtml = (booster, sizeClass) => booster.imagePath
        ? '<img src="' + escapeHtml(booster.imagePath) + '" alt="" class="' + sizeClass + '">'
        : '<div class="ao-opener-cover-fallback mx-auto"><i class="fa-solid fa-gift fa-3x"></i></div>';

    const renderGrid = (boosters) => {
        gridEl.innerHTML = '';
        boosters.forEach((booster) => {
            const col = document.createElement('div');
            col.className = 'col-6 col-md-3';
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ao-booster-tile w-100';
            btn.innerHTML =
                coverHtml(booster, 'ao-booster-cover-img') +
                '<div class="mt-2 fw-semibold">' + escapeHtml(booster.name) + '</div>' +
                '<div class="text-muted small">×' + booster.quantity + '</div>';
            btn.addEventListener('click', () => showBooster(booster));
            col.appendChild(btn);
            gridEl.appendChild(col);
        });
    };

    const loadBoosters = async () => {
        loadingEl.hidden = false;
        emptyEl.hidden = true;
        errorEl.hidden = true;
        try {
            const res = await fetch('/api/boosters', { credentials: 'same-origin' });
            loadingEl.hidden = true;

            if (res.status === 401) { anonEl.hidden = false; return; }
            if (!res.ok) {
                errorEl.hidden = false;
                errorEl.textContent = t('boosters.loadError', 'Could not load your boosters.');
                return;
            }

            const boosters = await res.json();
            if (!boosters.length) { emptyEl.hidden = false; gridEl.innerHTML = ''; return; }
            renderGrid(boosters);
        } catch {
            loadingEl.hidden = true;
            errorEl.hidden = false;
            errorEl.textContent = t('boosters.networkError', 'Network error.');
        }
    };

    // ---- Opening overlay ----
    // Step 1 (tile click): show the sealed booster enlarged, tilting with the
    // pointer — purely visual, no network call yet.
    // Step 2 (click on the enlarged cover): THIS is what actually draws the card
    // server-side (POST .../open), then plays the slide-away reveal once the card
    // is known. Drawing must happen here, not in step 1, so nothing is committed
    // until the player actually taps to open it.

    const backdrop = document.getElementById('ao-opener-backdrop');
    const coverEl = document.getElementById('ao-opener-cover');
    const cardEl = document.getElementById('ao-opener-card');

    let csrfToken = null;
    const fetchCsrfToken = async () => {
        try {
            const res = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
            if (res.ok) csrfToken = (await res.json()).token;
        } catch { /* leave null; the open call will surface the error */ }
    };

    const closeOverlay = () => {
        backdrop.hidden = true;
        coverEl.classList.remove('ao-opening');
        window.AO_CARD_TILT?.detach(coverEl);
        coverEl.onclick = null;
        coverEl.innerHTML = '';
        cardEl.innerHTML = '';
        loadBoosters();
    };
    backdrop?.addEventListener('click', (e) => {
        if (e.target === backdrop) closeOverlay();
    });

    const showBooster = (booster) => {
        coverEl.innerHTML = coverHtml(booster, '');
        coverEl.classList.remove('ao-opening');
        cardEl.innerHTML = '';
        window.AO_CARD_TILT?.attach(coverEl);
        coverEl.onclick = () => revealBooster(booster);
        backdrop.hidden = false;
    };

    const revealBooster = async (booster) => {
        coverEl.onclick = null; // no double-open while the request is in flight
        if (!csrfToken) await fetchCsrfToken();

        let opened;
        try {
            const res = await fetch(
                '/api/boosters/' + encodeURIComponent(booster.boosterTypeKey) + '/open?locale=' + encodeURIComponent(locale()),
                {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: Object.assign({ 'Content-Type': 'application/json' }, csrfToken ? { 'X-CSRF-TOKEN': csrfToken } : {}),
                    body: JSON.stringify({ quantity: 1 }),
                });
            if (!res.ok) {
                errorEl.hidden = false;
                errorEl.textContent = (await res.text()) || t('boosters.openError', 'Could not open this booster.');
                coverEl.onclick = () => revealBooster(booster); // allow retry
                return;
            }
            opened = await res.json();
        } catch {
            errorEl.hidden = false;
            errorEl.textContent = t('boosters.networkError', 'Network error.');
            coverEl.onclick = () => revealBooster(booster);
            return;
        }

        const card = opened[0];
        cardEl.innerHTML = card
            ? '<altered-card ref="' + escapeHtml(card.cardReference) + '" locale="' + escapeHtml(locale()) + '"></altered-card>'
            : '';
        window.AO_CARD_TILT?.detach(coverEl);
        coverEl.classList.add('ao-opening');
    };

    loadBoosters();
})();
