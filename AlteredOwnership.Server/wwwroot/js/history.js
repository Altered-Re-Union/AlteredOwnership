(() => {
    const escapeHtml = (s) => String(s).replace(/[&<>"']/g, (c) => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));

    const t = (key, fallback) => {
        const dict = window.AO_I18N || {};
        const lang = document.documentElement.lang || 'en';
        return (dict[lang] && dict[lang][key]) || (dict.en && dict.en[key]) || fallback;
    };

    const anonEl = document.getElementById('ao-history-anon');
    const loadingEl = document.getElementById('ao-history-loading');
    const emptyEl = document.getElementById('ao-history-empty');
    const errorEl = document.getElementById('ao-history-error');
    const listEl = document.getElementById('ao-history-list');

    const locale = () => document.documentElement.lang || 'en';

    const formatDate = (iso) => {
        try { return new Date(iso).toLocaleString(locale()); }
        catch { return iso; }
    };

    const cardLabel = (item) => (item.name || item.reference) + ' ×' + item.quantity;

    // Uniques have no catalog image (the Cards table only knows base printed
    // cards), so they're drawn live by the Altered-Card-Renderer web component
    // instead of a static <img>.
    const cardThumb = (item, sizePx) => item.isUnique
        ? '<altered-card ref="' + escapeHtml(item.reference) + '" locale="' + escapeHtml(locale()) + '" style="width:' + sizePx + 'px;"></altered-card>'
        : (item.imagePath
            ? '<img src="' + escapeHtml(item.imagePath) + '" alt="" style="width:' + sizePx + 'px;height:auto;">'
            : '');

    const renderPreview = (preview) => preview.map((item) =>
        '<span class="d-inline-flex align-items-center gap-1 small border rounded px-2 py-1">' +
            cardThumb(item, 40) +
            '<span>' + escapeHtml(cardLabel(item)) + '</span>' +
        '</span>').join(' ');

    // +received is (almost) always cards, -given is (almost) always the booster(s) that
    // were opened for them, so a generic card-back / booster icon next to each count reads
    // at a glance without needing the reference or booster name spelled out.
    const renderDelta = (evt) => {
        const parts = [];
        if (evt.received > 0) parts.push(
            '<span class="ao-delta d-inline-flex align-items-center gap-1">' +
                '<img src="/img/card-back.webp" alt="" class="ao-delta-icon">' +
                '<span class="text-success fw-semibold">+' + evt.received + '</span>' +
            '</span>');
        if (evt.given > 0) parts.push(
            '<span class="ao-delta d-inline-flex align-items-center gap-1">' +
                '<img src="/img/booster-icon.webp" alt="" class="ao-delta-icon">' +
                '<span class="text-danger fw-semibold">-' + evt.given + '</span>' +
            '</span>');
        return parts.join(' ');
    };

    const renderEventRow = (evt) => {
        const item = document.createElement('button');
        item.type = 'button';
        item.className = 'list-group-item list-group-item-action';
        item.innerHTML =
            '<div class="d-flex justify-content-between align-items-start gap-3">' +
                '<div>' +
                    '<div class="fw-semibold">' + escapeHtml(evt.name) + '</div>' +
                    '<div class="text-muted small mb-2">' + escapeHtml(formatDate(evt.createdAt)) + '</div>' +
                    '<div class="d-flex flex-wrap gap-2">' + renderPreview(evt.preview) + '</div>' +
                '</div>' +
                '<div class="text-nowrap">' + renderDelta(evt) + '</div>' +
            '</div>';
        // Exactly one card in this event (the common case: a single booster opened) — skip
        // the detail modal entirely and jump straight to that card's zoom. Anything else
        // (several cards, or none — e.g. only a booster grant) still needs the modal to
        // pick among lines.
        item.addEventListener('click', () => (evt.cardCount === 1 ? openSingleCardZoom(evt.id) : openDetail(evt.id)));
        return item;
    };

    // Modal
    const modalEl = document.getElementById('ao-event-modal');
    const modal = window.bootstrap ? new window.bootstrap.Modal(modalEl) : null;
    const modalTitle = document.getElementById('ao-event-modal-title');
    const modalBody = document.getElementById('ao-event-modal-body');

    // Zoom overlay: click a card in the detail modal to see it much bigger, with
    // the same pointer-tilt effect as opening a booster (card-tilt.js).
    const zoomBackdrop = document.getElementById('ao-card-zoom-backdrop');
    const zoomContent = document.getElementById('ao-card-zoom-content');

    const closeZoom = () => {
        if (!zoomBackdrop) return;
        zoomBackdrop.hidden = true;
        window.AO_CARD_TILT?.detach(zoomContent);
        zoomContent.innerHTML = '';
    };
    zoomBackdrop?.addEventListener('click', (e) => {
        if (e.target === zoomBackdrop) closeZoom();
    });

    const openZoom = (ref, isUnique, name, imagePath) => {
        if (!zoomBackdrop) return;
        zoomContent.innerHTML = isUnique
            ? '<altered-card ref="' + escapeHtml(ref) + '" locale="' + escapeHtml(locale()) + '"></altered-card>'
            : (imagePath
                ? '<img src="' + escapeHtml(imagePath) + '" alt="' + escapeHtml(name) + '" style="max-width:100%;max-height:100%;">'
                : '<div class="ao-opener-cover-fallback"><i class="fa-solid fa-image fa-3x"></i></div>');
        window.AO_CARD_TILT?.attach(zoomContent);
        zoomBackdrop.hidden = false;
    };

    // Single-card events (a booster opened, most commonly) skip the detail modal
    // entirely: fetch the detail just to resolve the one card's data, then go straight to
    // its zoom. Falls back to the normal modal if anything about that shortcut fails.
    const openSingleCardZoom = async (id) => {
        try {
            const res = await fetch('/api/history/' + encodeURIComponent(id), { credentials: 'same-origin' });
            if (!res.ok) { openDetail(id); return; }
            const detail = await res.json();
            const line = detail.received.find((l) => !l.isBooster) || detail.given.find((l) => !l.isBooster);
            if (!line) { openDetail(id); return; }
            openZoom(line.reference, line.isUnique, line.name, line.imagePath);
        } catch {
            openDetail(id);
        }
    };

    // Card thumbnails inside the modal aren't nested in another <button> (unlike the
    // list-row preview strip, which lives inside the row's own clickable button), so
    // they can safely be their own button that opens the zoom overlay on click.
    const clickableThumb = (item, sizePx) =>
        '<button type="button" class="ao-card-thumb-btn" data-ref="' + escapeHtml(item.reference) + '" ' +
            'data-unique="' + (item.isUnique ? '1' : '') + '" data-name="' + escapeHtml(item.name || item.reference) + '" ' +
            'data-image="' + escapeHtml(item.imagePath || '') + '">' +
            cardThumb(item, sizePx) +
        '</button>';

    modalBody?.addEventListener('click', (e) => {
        const btn = e.target.closest('.ao-card-thumb-btn');
        if (!btn) return;
        openZoom(btn.dataset.ref, btn.dataset.unique === '1', btn.dataset.name, btn.dataset.image);
    });

    const renderLineGroup = (title, lines, sign) => {
        if (!lines.length) return '';
        return '<h3 class="h6 mt-3">' + escapeHtml(title) + '</h3>' +
            '<ul class="list-unstyled d-flex flex-column gap-2">' +
            lines.map((l) =>
                '<li class="d-flex align-items-center gap-2">' +
                    clickableThumb(l, 72) +
                    '<span>' + escapeHtml(l.name || l.reference) + '</span>' +
                    '<span class="ms-auto ' + (sign === '+' ? 'text-success' : 'text-danger') + ' fw-semibold">' +
                        sign + l.quantity +
                    '</span>' +
                '</li>').join('') +
            '</ul>';
    };

    const openDetail = async (id) => {
        modalTitle.textContent = '…';
        modalBody.innerHTML = '<div class="text-muted">' + escapeHtml(t('history.loading', 'Loading…')) + '</div>';
        modal?.show();
        try {
            const res = await fetch('/api/history/' + encodeURIComponent(id), { credentials: 'same-origin' });
            if (!res.ok) {
                modalBody.innerHTML = '<div class="text-danger">' + escapeHtml(t('history.loadError', 'Could not load this event.')) + '</div>';
                return;
            }
            const detail = await res.json();
            modalTitle.textContent = detail.name;
            const receivedHtml = renderLineGroup(t('history.received', 'Cards received'), detail.received, '+');
            const givenHtml = renderLineGroup(t('history.given', 'Cards given'), detail.given, '-');
            modalBody.innerHTML = receivedHtml + givenHtml
                || '<div class="text-muted">' + escapeHtml(t('history.empty', 'No events yet.')) + '</div>';
        } catch {
            modalBody.innerHTML = '<div class="text-danger">' + escapeHtml(t('history.networkError', 'Network error.')) + '</div>';
        }
    };

    (async () => {
        try {
            const res = await fetch('/api/history?locale=' + encodeURIComponent(locale()), { credentials: 'same-origin' });
            loadingEl.hidden = true;

            if (res.status === 401) { anonEl.hidden = false; return; }
            if (!res.ok) {
                errorEl.hidden = false;
                errorEl.textContent = t('history.loadError', 'Could not load your history.');
                return;
            }

            const events = await res.json();
            if (!events.length) { emptyEl.hidden = false; return; }

            events.forEach((evt) => listEl.appendChild(renderEventRow(evt)));
        } catch {
            loadingEl.hidden = true;
            errorEl.hidden = false;
            errorEl.textContent = t('history.networkError', 'Network error.');
        }
    })();
})();
