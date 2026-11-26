/* Mobile filter panel. Without JS the form stays visible. */
(function () {
    'use strict';
    var toggle = document.getElementById('filter-toggle');
    var panel = document.getElementById('filter-panel');
    if (!toggle || !panel) return;

    var compact = window.matchMedia('(max-width: 1023px)');
    var mobileOpen = false;
    var closeButton = document.getElementById('filter-close');
    var pricePresets = panel.querySelector('.price-presets');
    pricePresets.hidden = false;
    pricePresets.addEventListener('click', function (event) {
        var button = event.target.closest('[data-price-min]');
        if (!button) return;
        panel.elements.GiaTu.value = button.dataset.priceMin;
        panel.elements.GiaDen.value = button.dataset.priceMax;
    });

    function sync() {
        var open = !compact.matches || mobileOpen;
        panel.hidden = !open;
        panel.classList.toggle('filter-drawer-open', compact.matches && mobileOpen);
        toggle.setAttribute('aria-expanded', String(open));
    }
    function close() {
        if (!compact.matches || !mobileOpen) return;
        mobileOpen = false;
        sync();
        toggle.focus();
    }

    toggle.classList.add('is-ready');
    toggle.addEventListener('click', function () {
        if (mobileOpen) {
            close();
            return;
        }
        mobileOpen = true;
        sync();
        closeButton.focus();
    });
    closeButton.addEventListener('click', close);
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') close();
    });
    if (compact.addEventListener) compact.addEventListener('change', sync);
    else compact.addListener(sync);
    sync();
})();
