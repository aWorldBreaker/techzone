// Keep the theme controls and stored preference in sync.
(function () {
    'use strict';
    var KEY = 'techzone-theme';
    var btn = document.getElementById('theme-toggle');
    if (!btn) return;

    function current() {
        return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    }
    function sync() {
        var dark = current() === 'dark';
        btn.setAttribute('aria-label', dark ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối');
        btn.setAttribute('aria-pressed', dark ? 'true' : 'false');
    }
    btn.addEventListener('click', function () {
        var next = current() === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        try { localStorage.setItem(KEY, next); } catch (e) { /* bỏ qua */ }
        sync();
        document.dispatchEvent(new CustomEvent('techzone:themechange', { detail: { theme: next } }));
    });
    sync();
})();
