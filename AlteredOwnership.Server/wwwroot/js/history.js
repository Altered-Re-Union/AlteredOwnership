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

    const renderPreview = (preview) => preview.map((item) =>
        '<span class="d-inline-flex align-items-center gap-1 small border rounded px-2 py-1">' +
            (item.imagePath
                ? '<img src="' + escapeHtml(item.imagePath) + '" alt="" style="width:22px;height:auto;">'
                : '') +
            '<span>' + escapeHtml(cardLabel(item)) + '</span>' +
        '</span>').join(' ');

    const renderDelta = (evt) => {
        const parts = [];
        if (evt.received > 0) parts.push('<span class="text-success fw-semibold">+' + evt.received + '</span>');
        if (evt.given > 0) parts.push('<span class="text-danger fw-semibold">-' + evt.given + '</span>');
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
        item.addEventListener('click', () => openDetail(evt.id));
        return item;
    };

    // Modal
    const modalEl = document.getElementById('ao-event-modal');
    const modal = window.bootstrap ? new window.bootstrap.Modal(modalEl) : null;
    const modalTitle = document.getElementById('ao-event-modal-title');
    const modalBody = document.getElementById('ao-event-modal-body');

    const renderLineGroup = (title, lines, sign) => {
        if (!lines.length) return '';
        return '<h3 class="h6 mt-3">' + escapeHtml(title) + '</h3>' +
            '<ul class="list-unstyled d-flex flex-column gap-2">' +
            lines.map((l) =>
                '<li class="d-flex align-items-center gap-2">' +
                    (l.imagePath
                        ? '<img src="' + escapeHtml(l.imagePath) + '" alt="" style="width:36px;height:auto;">'
                        : '') +
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
