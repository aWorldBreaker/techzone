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
        var drawerOpen = compact.matches && mobileOpen;
        panel.classList.toggle('filter-drawer-open', drawerOpen);
        if (drawerOpen) {
            panel.setAttribute('role', 'dialog');
            panel.setAttribute('aria-modal', 'true');
        } else {
            panel.removeAttribute('role');
            panel.removeAttribute('aria-modal');
        }
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
        if (!compact.matches || !mobileOpen) return;
        if (e.key === 'Escape') close();
        if (e.key !== 'Tab') return;
        var controls = Array.from(panel.querySelectorAll('button, input, select, a[href]'))
            .filter(function (control) {
                return !control.disabled && control.getClientRects().length > 0 &&
                    (control.type !== 'radio' || control.checked);
            });
        var first = controls[0];
        var last = controls[controls.length - 1];
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    });
    if (compact.addEventListener) compact.addEventListener('change', sync);
    else compact.addListener(sync);
    sync();
})();
