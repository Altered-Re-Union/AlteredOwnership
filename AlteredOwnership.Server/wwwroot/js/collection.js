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

    // Prettifies a raw catalog code (e.g. "CHARACTER", "FOIL_ART") for display —
    // these come straight from the Altered API with no per-language label of their own.
    const prettify = (s) => String(s).replace(/_/g, ' ').toLowerCase()
        .replace(/\b\w/g, (c) => c.toUpperCase());

    // Faction names are proper nouns, identical in every locale — no i18n entry needed.
    const FACTIONS = [
        ['AX', 'Axiom'], ['BR', 'Bravos'], ['LY', 'Lyra'],
        ['MU', 'Muna'], ['OR', 'Ordis'], ['YZ', 'Yzmir'],
    ];
    // Newest set first — matches altered.re's own set-picker order exactly.
    const SET_CODES = ['FUGUE', 'EOLE', 'DUSTER', 'CYCLONE', 'BISE', 'ALIZE', 'COREKS', 'CORE'];
    const SETS = () => SET_CODES.map((code) => [code, t('set.' + code, code)]);
    // Single-letter labels, fixed across every locale — matches altered.re's own compact
    // rarity chips, and keeps faction+rarity fitting on one shared row. The full localized
    // name still shows up as the button's tooltip (see iconToggleRow's title support).
    const RARITY_CODES = ['COMMON', 'RARE', 'UNIQUE', 'EXALTED'];
    const RARITY_LETTERS = { COMMON: 'C', RARE: 'R', UNIQUE: 'U', EXALTED: 'E' };
    const RARITIES = () => RARITY_CODES.map((code) => [code, RARITY_LETTERS[code], t('rarity.' + code, code)]);
    const RARITY_ICONS = {
        COMMON: '/img/rarities/common.png',
        RARE: '/img/rarities/rare.png',
        UNIQUE: '/img/rarities/unique.png',
        EXALTED: '/img/rarities/exalted.png',
    };

    const anonEl = document.getElementById('ao-collection-anon');
    const appEl = document.getElementById('ao-collection-app');
    const loadingEl = document.getElementById('ao-collection-loading');
    const emptyEl = document.getElementById('ao-collection-empty');
    const errorEl = document.getElementById('ao-collection-error');
    const gridEl = document.getElementById('ao-collection-grid');
    const countEl = document.getElementById('ao-collection-count');

    const nameInput = document.getElementById('ao-filter-name');
    const factionRowEl = document.getElementById('ao-filter-faction');
    const setRowEl = document.getElementById('ao-filter-set');
    const rarityRowEl = document.getElementById('ao-filter-rarity');
    const typeRowEl = document.getElementById('ao-filter-type-row');
    const variationSelect = document.getElementById('ao-filter-variation');
    const subtypeSelect = document.getElementById('ao-filter-subtype');
    const resetBtn = document.getElementById('ao-filter-reset');

    const numericIds = ['maincost', 'recallcost', 'forest', 'mountain', 'ocean'];
    const numericInputs = {};
    numericIds.forEach((id) => {
        numericInputs[id] = {
            min: document.getElementById('ao-filter-' + id + '-min'),
            max: document.getElementById('ao-filter-' + id + '-max'),
        };
    });

    let allCards = [];
    const activeFactions = new Set();
    const activeSets = new Set();
    const activeRarities = new Set();
    const activeTypes = new Set();

    // Shared click/active-state wiring for both toggle-row styles below.
    const wireToggle = (btn, code, activeSet) => {
        btn.type = 'button';
        btn.addEventListener('click', () => {
            if (activeSet.has(code)) activeSet.delete(code); else activeSet.add(code);
            btn.classList.toggle('active');
            applyFilters();
        });
    };

    // Faction/type: plain icon+text pill (type has no icon set, so just text). Rarity uses
    // the compact variant — a small gem icon, tighter padding, bolder text — matching
    // altered.re's own rarity chips.
    const iconToggleRow = (container, entries, activeSet, iconPath, compact) => {
        container.innerHTML = '';
        entries.forEach(([code, label, title]) => {
            const btn = document.createElement('button');
            btn.className = 'ao-icon-filter-btn' + (compact ? ' ao-icon-filter-btn--compact' : '');
            if (title) btn.title = title;
            btn.innerHTML = (iconPath(code) ? '<img src="' + iconPath(code) + '" alt="">' : '') +
                '<span>' + escapeHtml(label) + '</span>';
            wireToggle(btn, code, activeSet);
            container.appendChild(btn);
        });
    };

    // Set/edition: a tile with the key-art as the button's own background and the label
    // captioned over it — ported from altered.re's own set picker. Faction stays a small
    // icon pill (see iconToggleRow below) — altered.re treats those two differently.
    const imageToggleRow = (container, entries, activeSet, imagePath) => {
        container.innerHTML = '';
        entries.forEach(([code, label]) => {
            const btn = document.createElement('button');
            btn.className = 'ao-image-filter-btn';
            const art = imagePath(code);
            if (art) btn.style.backgroundImage = 'url(\'' + art + '\')';
            const labelEl = document.createElement('span');
            labelEl.textContent = label;
            btn.appendChild(labelEl);
            wireToggle(btn, code, activeSet);
            container.appendChild(btn);
        });
    };

    const multiSelectValues = (select) => Array.from(select.selectedOptions).map((o) => o.value);

    const populateSelect = (select, values) => {
        const previous = new Set(multiSelectValues(select));
        select.innerHTML = '';
        values.forEach((v) => {
            const opt = document.createElement('option');
            opt.value = v;
            opt.textContent = prettify(v);
            if (previous.has(v)) opt.selected = true;
            select.appendChild(opt);
        });
    };

    // Type/variation/subtype options aren't a fixed known list — derive them from
    // whatever the user actually owns instead of guessing the Altered API's vocabulary.
    const refreshDynamicFacets = () => {
        const types = new Set();
        const variations = new Set();
        const subtypes = new Set();
        allCards.forEach((c) => {
            if (c.cardType) types.add(c.cardType);
            if (c.variation) variations.add(c.variation);
            (c.subTypes || []).forEach((s) => subtypes.add(s));
        });
        iconToggleRow(typeRowEl, Array.from(types).sort().map((v) => [v, t('cardType.' + v, prettify(v))]), activeTypes, () => null);
        populateSelect(variationSelect, Array.from(variations).sort());
        populateSelect(subtypeSelect, Array.from(subtypes).sort());
    };

    const numericFilter = (id) => {
        const min = numericInputs[id].min.value;
        const max = numericInputs[id].max.value;
        return { min: min === '' ? null : Number(min), max: max === '' ? null : Number(max) };
    };
    const passesNumeric = (value, filter) => {
        if (filter.min === null && filter.max === null) return true;
        if (value === null || value === undefined) return false;
        if (filter.min !== null && value < filter.min) return false;
        if (filter.max !== null && value > filter.max) return false;
        return true;
    };

    // Uniques have no catalog image of their own — same reasoning as history.js's
    // cardThumb/openZoom below: always draw them live via the Altered-Card-Renderer web
    // component rather than a static <img>, even when a (broken/unreachable) imagePath
    // happens to be present from a catalog join.
    const cardThumb = (item) => item.isUnique
        ? '<altered-card ref="' + escapeHtml(item.reference) + '" locale="' + escapeHtml(locale()) + '" style="width:100%;"></altered-card>'
        : (item.imagePath
            ? '<img src="' + escapeHtml(item.imagePath) + '" alt="" class="ao-collection-cover-img">'
            : '<div class="ao-opener-cover-fallback mx-auto"><i class="fa-solid fa-image fa-3x"></i></div>');

    const renderGrid = (cards) => {
        gridEl.innerHTML = '';
        cards.forEach((card) => {
            const col = document.createElement('div');
            col.className = 'col-6 col-md-3';
            const tile = document.createElement('button');
            tile.type = 'button';
            tile.className = 'ao-collection-tile w-100';
            // The card's own art already shows its name — no need to repeat it as text.
            tile.setAttribute('aria-label', card.name || card.reference);
            tile.title = card.name || card.reference;
            tile.innerHTML =
                cardThumb(card) +
                '<span class="ao-collection-qty-badge"><img src="/img/card-back.webp" alt="">×' + card.quantity + '</span>';
            tile.addEventListener('click', () => openZoom(card));
            col.appendChild(tile);
            gridEl.appendChild(col);
        });
    };

    const applyFilters = () => {
        const name = nameInput.value.trim().toLowerCase();
        const variations = new Set(multiSelectValues(variationSelect));
        const subtypes = new Set(multiSelectValues(subtypeSelect));
        const mainCost = numericFilter('maincost');
        const recallCost = numericFilter('recallcost');
        const forest = numericFilter('forest');
        const mountain = numericFilter('mountain');
        const ocean = numericFilter('ocean');

        const filtered = allCards.filter((c) => {
            if (name && !(c.name || '').toLowerCase().includes(name)) return false;
            if (activeFactions.size && !activeFactions.has(c.faction)) return false;
            if (activeSets.size && !activeSets.has(c.set)) return false;
            if (activeRarities.size && !activeRarities.has(c.rarity)) return false;
            if (activeTypes.size && !activeTypes.has(c.cardType)) return false;
            if (variations.size && !variations.has(c.variation)) return false;
            if (subtypes.size && !(c.subTypes || []).some((s) => subtypes.has(s))) return false;
            if (!passesNumeric(c.mainCost, mainCost)) return false;
            if (!passesNumeric(c.recallCost, recallCost)) return false;
            if (!passesNumeric(c.forest, forest)) return false;
            if (!passesNumeric(c.mountain, mountain)) return false;
            if (!passesNumeric(c.ocean, ocean)) return false;
            return true;
        });

        emptyEl.hidden = filtered.length > 0;
        countEl.textContent = filtered.length + ' / ' + allCards.length;
        renderGrid(filtered);
    };

    nameInput.addEventListener('input', applyFilters);
    [variationSelect, subtypeSelect].forEach((sel) => sel.addEventListener('change', applyFilters));
    numericIds.forEach((id) => {
        numericInputs[id].min.addEventListener('input', applyFilters);
        numericInputs[id].max.addEventListener('input', applyFilters);
    });

    resetBtn.addEventListener('click', () => {
        nameInput.value = '';
        activeFactions.clear();
        activeSets.clear();
        activeRarities.clear();
        activeTypes.clear();
        factionRowEl.querySelectorAll('.active').forEach((b) => b.classList.remove('active'));
        setRowEl.querySelectorAll('.active').forEach((b) => b.classList.remove('active'));
        rarityRowEl.querySelectorAll('.active').forEach((b) => b.classList.remove('active'));
        typeRowEl.querySelectorAll('.active').forEach((b) => b.classList.remove('active'));
        variationSelect.querySelectorAll('option').forEach((o) => { o.selected = false; });
        subtypeSelect.querySelectorAll('option').forEach((o) => { o.selected = false; });
        numericIds.forEach((id) => { numericInputs[id].min.value = ''; numericInputs[id].max.value = ''; });
        applyFilters();
    });

    // Zoom overlay — same pointer-tilt effect as the booster opener and history's card
    // zoom (card-tilt.js), but no pack-cover step: just the card, as big as possible.
    const zoomBackdrop = document.getElementById('ao-opener-backdrop');
    const zoomContent = document.getElementById('ao-opener-card');
    const closeZoom = () => {
        zoomBackdrop.hidden = true;
        window.AO_CARD_TILT?.detach(zoomContent);
        zoomContent.innerHTML = '';
    };
    zoomBackdrop?.addEventListener('click', (e) => {
        if (e.target === zoomBackdrop) closeZoom();
    });
    const openZoom = (card) => {
        zoomContent.innerHTML = card.isUnique
            ? '<altered-card ref="' + escapeHtml(card.reference) + '" locale="' + escapeHtml(locale()) + '"></altered-card>'
            : (card.imagePath
                ? '<img src="' + escapeHtml(card.imagePath) + '" alt="' + escapeHtml(card.name || '') + '" style="max-width:100%;max-height:100%;">'
                : '<div class="ao-opener-cover-fallback"><i class="fa-solid fa-image fa-3x"></i></div>');
        window.AO_CARD_TILT?.attach(zoomContent, { holo: card.isUnique });
        zoomBackdrop.hidden = false;
    };

    (async () => {
        try {
            const res = await fetch('/api/collection?locale=' + encodeURIComponent(locale()), { credentials: 'same-origin' });
            loadingEl.hidden = true;

            if (res.status === 401) { anonEl.hidden = false; return; }
            if (!res.ok) {
                errorEl.hidden = false;
                errorEl.textContent = t('collection.loadError', 'Could not load your collection.');
                return;
            }

            allCards = await res.json();
            appEl.hidden = false;

            iconToggleRow(factionRowEl, FACTIONS, activeFactions, (code) => '/img/factions/' + code + '.webp');
            imageToggleRow(setRowEl, SETS(), activeSets, (code) => '/img/sets/' + code + '.webp');
            iconToggleRow(rarityRowEl, RARITIES(), activeRarities, (code) => RARITY_ICONS[code], true);
            refreshDynamicFacets();
            applyFilters();
        } catch {
            loadingEl.hidden = true;
            errorEl.hidden = false;
            errorEl.textContent = t('collection.networkError', 'Network error.');
        }
    })();
})();
