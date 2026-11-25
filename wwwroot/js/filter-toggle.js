/* Mobile filter panel. Without JS the form stays visible. */
(function () {
    'use strict';
    var toggle = document.getElementById('filter-toggle');
    var panel = document.getElementById('filter-panel');
    if (!toggle || !panel) return;

    var compact = window.matchMedia('(max-width: 1024px)');
    var mobileOpen = false;

    function sync() {
        var open = !compact.matches || mobileOpen;
        panel.hidden = !open;
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
        mobileOpen = !mobileOpen;
        sync();
    });
    document.getElementById('filter-close').addEventListener('click', close);
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') close();
    });
    if (compact.addEventListener) compact.addEventListener('change', sync);
    else compact.addListener(sync);
    sync();
})();
