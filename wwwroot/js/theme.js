/* TechZone — chuyển sáng/tối: nút #theme-toggle ở header, lưu localStorage
   'techzone-theme'. Lần đầu mở trang, script trong <head> của _Layout đã đặt
   data-theme (theo localStorage, chưa có thì theo prefers-color-scheme)
   trước khi trang vẽ nên không nháy sáng. WebGL đọc theme theo từng khung
   hình nên tự thích nghi, không cần báo riêng; vẫn bắn event
   'techzone:themechange' cho code khác nếu cần. */
(function () {
    'use strict';
    var KEY = 'techzone-theme';
    var btn = document.getElementById('theme-toggle');
    if (!btn) return;
    var icon = btn.querySelector('[aria-hidden]');

    function current() {
        return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    }
    function sync() {
        var dark = current() === 'dark';
        if (icon) icon.textContent = dark ? '☀️' : '🌙';
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
