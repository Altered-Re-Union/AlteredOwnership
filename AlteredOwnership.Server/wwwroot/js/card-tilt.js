// Pointer + device-tilt holo effect (booster cover, revealed card, history zoom),
// ported in full from altered-draft's useHoloTilt (src/components/CardZoom.jsx) — pointer
// tilt, the deviceorientation-driven mobile fallback, and the holo shine/glare layers —
// so this app's cards behave the same as altered-draft's instead of re-adding pieces of
// the same effect one bug report at a time. Writes --ao-tilt-x/-y (rotation) and
// --ao-tilt-px/-py/--ao-tilt-opacity (shine/glare position + visibility) as CSS custom
// properties on the attached element; app.css's .ao-tilt-card / .ao-tilt-holo rules
// consume them. --ao-tilt-y feeds rotateY (horizontal pointer position) and --ao-tilt-x
// feeds rotateX (vertical pointer position), matching altered-draft's --rotate-x/-y
// mapping and sign so the near corner leans toward the pointer instead of away from it.
window.AO_CARD_TILT = (() => {
    const clamp = (v, min, max) => Math.min(max, Math.max(min, v));

    // holo:true elements get the shine/glare gradient layers (gold sheen for uniques);
    // plain attach() is tilt-only, matching altered-draft's Common-rarity treatment.
    const ensureHoloLayers = (el) => {
        if (el.querySelector(':scope > .ao-tilt-shine')) return;
        const shine = document.createElement('div');
        shine.className = 'ao-tilt-shine';
        const glare = document.createElement('div');
        glare.className = 'ao-tilt-glare';
        el.append(shine, glare);
    };

    const setFromPercent = (el, px, py) => {
        el.style.setProperty('--ao-tilt-px', px + '%');
        el.style.setProperty('--ao-tilt-py', py + '%');
        el.style.setProperty('--ao-tilt-y', (-((px - 50) / 3.5)) + 'deg');
        el.style.setProperty('--ao-tilt-x', ((py - 50) / 3.5) + 'deg');
        el.style.setProperty('--ao-tilt-opacity', '1');
    };

    const apply = (el, clientX, clientY) => {
        const rect = el.getBoundingClientRect();
        const px = clamp(((clientX - rect.left) / rect.width) * 100, 0, 100);
        const py = clamp(((clientY - rect.top) / rect.height) * 100, 0, 100);
        setFromPercent(el, px, py);
    };

    const reset = (el) => {
        el.style.setProperty('--ao-tilt-px', '50%');
        el.style.setProperty('--ao-tilt-py', '50%');
        el.style.setProperty('--ao-tilt-x', '0deg');
        el.style.setProperty('--ao-tilt-y', '0deg');
        el.style.setProperty('--ao-tilt-opacity', '0');
    };

    // Phones without a mouse get the effect driven by device tilt instead — same gamma/beta
    // mapping and clamp as altered-draft. One shared listener drives every attached element
    // at once (there's normally only ever one on screen), added lazily so pages that never
    // attach anything never pay for it.
    const orientationTargets = new Set();
    let orientationListening = false;
    const onOrientation = (e) => {
        if (e.beta == null || e.gamma == null) return;
        const x = clamp(e.gamma, -18, 18);
        const y = clamp(e.beta - 45, -18, 18);
        const px = ((x + 18) / 36) * 100;
        const py = ((y + 18) / 36) * 100;
        orientationTargets.forEach((el) => {
            el.style.setProperty('--ao-tilt-px', px + '%');
            el.style.setProperty('--ao-tilt-py', py + '%');
            el.style.setProperty('--ao-tilt-x', (-x) + 'deg');
            el.style.setProperty('--ao-tilt-y', y + 'deg');
            el.style.setProperty('--ao-tilt-opacity', '1');
        });
    };
    const ensureOrientationListener = () => {
        if (orientationListening) return;
        orientationListening = true;
        window.addEventListener('deviceorientation', onOrientation);
    };

    const attach = (el, { holo = false } = {}) => {
        el.classList.toggle('ao-tilt-holo', holo);
        if (holo) ensureHoloLayers(el);
        el.onpointermove = (e) => apply(el, e.clientX, e.clientY);
        el.onpointerleave = () => reset(el);
        orientationTargets.add(el);
        ensureOrientationListener();
    };

    const detach = (el) => {
        el.onpointermove = null;
        el.onpointerleave = null;
        orientationTargets.delete(el);
        // ao-tilt-holo's halo box-shadow isn't opacity-gated (it's meant to sit on the card
        // permanently, not just while tilting) — leaving the class on after detach left it
        // glowing around the next, still-empty card container until something holo was
        // attached again.
        el.classList.remove('ao-tilt-holo');
        reset(el);
    };

    return { attach, detach, reset };
})();
