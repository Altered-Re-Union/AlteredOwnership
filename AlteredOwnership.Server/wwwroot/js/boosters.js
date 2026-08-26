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

    let boosterList = [];

    const renderGrid = (boosters) => {
        boosterList = boosters;
        gridEl.innerHTML = '';
        boosters.forEach((booster, index) => {
            const col = document.createElement('div');
            col.className = 'col-6 col-md-3';
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ao-booster-tile w-100';
            btn.innerHTML =
                coverHtml(booster, 'ao-booster-cover-img') +
                '<div class="mt-2 fw-semibold">' + escapeHtml(booster.name) + '</div>' +
                '<div class="text-muted small">×' + booster.quantity + '</div>';
            btn.addEventListener('click', () => openerAt(index));
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
    // Step 1 (tile click, or prev/next arrow): show the sealed booster enlarged,
    // tilting with the pointer — purely visual, no network call yet. Prev/next
    // browse other booster TYPES in the grid the same way, always landing back
    // on a sealed cover (never re-opening one already drawn this session).
    // Step 2 (click the cover, or the Open button): THIS is what actually draws
    // the card server-side (POST .../open), then plays the slide-away reveal
    // once the card is known. Drawing must happen here, not in step 1, so
    // nothing is committed until the player actually taps to open it.

    const backdrop = document.getElementById('ao-opener-backdrop');
    const coverEl = document.getElementById('ao-opener-cover');
    const rotatorEl = document.getElementById('ao-opener-rotator');
    const cardEl = document.getElementById('ao-opener-card');
    const prevBtn = document.getElementById('ao-opener-prev');
    const nextBtn = document.getElementById('ao-opener-next');
    const infoEl = document.getElementById('ao-opener-info');
    const nameEl = document.getElementById('ao-opener-name');
    const qtyEl = document.getElementById('ao-opener-qty');
    const openBtn = document.getElementById('ao-opener-open-btn');

    let currentIndex = -1;

    let csrfToken = null;
    const fetchCsrfToken = async () => {
        try {
            const res = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
            if (res.ok) csrfToken = (await res.json()).token;
        } catch { /* leave null; the open call will surface the error */ }
    };

    const updateNav = () => {
        prevBtn.disabled = currentIndex <= 0;
        nextBtn.disabled = currentIndex >= boosterList.length - 1;
    };

    const closeOverlay = () => {
        backdrop.hidden = true;
        coverEl.classList.remove('ao-opening');
        window.AO_CARD_TILT?.detach(coverEl);
        coverEl.onclick = null;
        openBtn.onclick = null;
        rotatorEl.innerHTML = '';
        cardEl.innerHTML = '';
        currentIndex = -1;
        loadBoosters();
    };
    backdrop?.addEventListener('click', (e) => {
        if (e.target === backdrop) closeOverlay();
    });

    // Resets the overlay to the sealed cover for boosterList[index] — used both
    // to open the overlay from the grid and to browse via prev/next.
    const showSealed = (index) => {
        currentIndex = index;
        const booster = boosterList[index];
        rotatorEl.innerHTML = coverHtml(booster, '');
        coverEl.classList.remove('ao-opening');
        cardEl.innerHTML = '';
        infoEl.hidden = false;
        nameEl.textContent = booster.name;
        qtyEl.textContent = '×' + booster.quantity;
        window.AO_CARD_TILT?.attach(coverEl);
        coverEl.onclick = () => revealBooster(booster);
        openBtn.onclick = () => revealBooster(booster);
        updateNav();
    };

    const openerAt = (index) => {
        if (!boosterList.length) return;
        showSealed(Math.max(0, Math.min(index, boosterList.length - 1)));
        backdrop.hidden = false;
    };
    prevBtn?.addEventListener('click', () => openerAt(currentIndex - 1));
    nextBtn?.addEventListener('click', () => openerAt(currentIndex + 1));

    document.addEventListener('keydown', (e) => {
        if (backdrop.hidden) return;
        if (e.key === 'Escape') closeOverlay();
        else if (e.key === 'ArrowLeft') openerAt(currentIndex - 1);
        else if (e.key === 'ArrowRight') openerAt(currentIndex + 1);
    });

    const revealBooster = async (booster) => {
        coverEl.onclick = null; // no double-open while the request is in flight
        openBtn.onclick = null;
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
                openBtn.onclick = () => revealBooster(booster);
                return;
            }
            opened = await res.json();
        } catch {
            errorEl.hidden = false;
            errorEl.textContent = t('boosters.networkError', 'Network error.');
            coverEl.onclick = () => revealBooster(booster);
            openBtn.onclick = () => revealBooster(booster);
            return;
        }

        const card = opened[0];
        cardEl.innerHTML = card
            ? '<altered-card ref="' + escapeHtml(card.cardReference) + '" locale="' + escapeHtml(locale()) + '"></altered-card>'
            : '';
        window.AO_CARD_TILT?.detach(coverEl);
        coverEl.classList.add('ao-opening');
        infoEl.hidden = true;
    };

    loadBoosters();
})();
