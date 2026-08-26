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
    const openerLoadingEl = document.getElementById('ao-opener-loading');

    let currentIndex = -1;

    // The <altered-card> web component draws its art onto a <canvas> it inserts itself,
    // asynchronously, with no load/ready event of its own to hook into. Wait for that
    // canvas to actually appear (plus a couple of frames for it to finish painting) before
    // sliding the cover away, so the reveal doesn't uncover an empty/loading card — capped
    // at a few seconds so a slow/broken renderer never leaves the cover stuck shut.
    const waitForCardReady = (hostEl, timeoutMs = 4000) => new Promise((resolve) => {
        const settle = () => requestAnimationFrame(() => requestAnimationFrame(resolve));
        if (hostEl.querySelector('canvas')) { settle(); return; }
        const observer = new MutationObserver(() => {
            if (!hostEl.querySelector('canvas')) return;
            observer.disconnect();
            clearTimeout(timer);
            settle();
        });
        observer.observe(hostEl, { childList: true, subtree: true });
        const timer = setTimeout(() => { observer.disconnect(); resolve(); }, timeoutMs);
    });

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
        window.AO_CARD_TILT?.detach(cardEl);
        coverEl.onclick = null;
        openBtn.onclick = null;
        openerLoadingEl.hidden = true;
        rotatorEl.innerHTML = '';
        cardEl.innerHTML = '';
        currentIndex = -1;
        // boosterList is already current — every open decrements it in place and
        // re-renders (see revealBooster) — so this just re-paints from local state. A
        // fresh loadBoosters() fetch here briefly showed the page's "Loading…" text above
        // the (still-visible, now stale) grid before swapping it, which visibly shifted
        // the grid down and back up on every close.
        renderGrid(boosterList);
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
        window.AO_CARD_TILT?.detach(cardEl);
        cardEl.innerHTML = '';
        infoEl.hidden = false;
        nameEl.textContent = booster.name;
        qtyEl.textContent = '×' + booster.quantity;
        // Pointer/gyro tilt only (no holo shine) on the sealed pack — matches altered-draft's
        // plain-rarity treatment. Attached to coverEl (the full click box), not rotatorEl:
        // --ao-tilt-x/-y are written here and picked up by rotatorEl's own rotate transform
        // through CSS custom-property inheritance, keeping that transform free for coverEl's
        // own translateY slide-away animation instead of fighting over one `transform`.
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
        // Opening (server round trip) plus rendering the drawn card can visibly take a
        // couple of seconds — without this, a click just does nothing for that whole
        // stretch and reads as broken rather than working.
        openerLoadingEl.hidden = false;
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
                openerLoadingEl.hidden = true;
                errorEl.hidden = false;
                errorEl.textContent = (await res.text()) || t('boosters.openError', 'Could not open this booster.');
                coverEl.onclick = () => revealBooster(booster); // allow retry
                openBtn.onclick = () => revealBooster(booster);
                return;
            }
            opened = await res.json();
        } catch {
            openerLoadingEl.hidden = true;
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
        if (card) await waitForCardReady(cardEl);
        openerLoadingEl.hidden = true;
        coverEl.classList.add('ao-opening');
        // The slide-down only moves the cover off screen visually — the pack art (which
        // bleeds past the cover's own box, see .ao-opener-cover img) can otherwise still
        // peek back into view, e.g. behind the revealed card on a short viewport. Once the
        // slide finishes, drop it for real. Guarded on the class still being set: if the
        // player has since browsed to another booster, showSealed() already replaced
        // rotatorEl's content and removed this class — this stale event must not wipe that.
        coverEl.addEventListener('transitionend', () => {
            if (coverEl.classList.contains('ao-opening')) rotatorEl.innerHTML = '';
        }, { once: true });
        infoEl.hidden = true;
        // Every booster draws a unique — always eligible for the gold holo shine.
        if (card) window.AO_CARD_TILT?.attach(cardEl, { holo: true });

        // Reflect the draw in the grid right away — it's still visible behind the dimmed
        // backdrop, and waiting until the overlay closes to update it reads as stale/wrong.
        booster.quantity -= 1;
        renderGrid(boosterList);
        qtyEl.textContent = '×' + booster.quantity;
        window.AO_REFRESH_BOOSTERS_BADGE?.();
    };

    loadBoosters();
})();
