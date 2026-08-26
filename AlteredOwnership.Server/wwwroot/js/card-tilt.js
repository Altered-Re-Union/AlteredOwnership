// Shared pointer-tilt effect (booster cover, zoomed history cards): tracks
// pointermove over an element and writes it as --ao-tilt-x/--ao-tilt-y CSS custom
// properties on that same element, consumed by app.css's .ao-tilt-card / booster
// opener rules. Ported from altered-draft's useHoloTilt, trimmed to just the tilt
// (no holo shine layers, which this app has no use for). --ao-tilt-y feeds
// rotateY (horizontal pointer position) and --ao-tilt-x feeds rotateX (vertical
// pointer position) — same variable-to-axis mapping and sign as the original, so
// the near corner leans toward the pointer instead of away from it.
window.AO_CARD_TILT = (() => {
    const apply = (el, clientX, clientY) => {
        const rect = el.getBoundingClientRect();
        const px = Math.min(100, Math.max(0, ((clientX - rect.left) / rect.width) * 100));
        const py = Math.min(100, Math.max(0, ((clientY - rect.top) / rect.height) * 100));
        el.style.setProperty('--ao-tilt-y', (-((px - 50) / 3.5)) + 'deg');
        el.style.setProperty('--ao-tilt-x', ((py - 50) / 3.5) + 'deg');
    };

    const reset = (el) => {
        el.style.setProperty('--ao-tilt-x', '0deg');
        el.style.setProperty('--ao-tilt-y', '0deg');
    };

    const attach = (el) => {
        el.onpointermove = (e) => apply(el, e.clientX, e.clientY);
        el.onpointerleave = () => reset(el);
    };

    const detach = (el) => {
        el.onpointermove = null;
        el.onpointerleave = null;
        reset(el);
    };

    return { attach, detach, reset };
})();
