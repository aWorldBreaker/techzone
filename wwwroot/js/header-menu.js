/* TechZone — Sprint 6 Module 1: hamburger menu mobile + dropdown tài khoản desktop.
   - #menu-toggle mở/đóng #mobile-menu (aria-expanded, Esc đóng, bấm link thì tự đóng).
   - #user-menu-btn mở/đóng #user-menu-panel (aria-expanded, Esc đóng, bấm ra ngoài đóng).
   - Không JS: menu mobile và dropdown không mở được (nút vẫn focus được, panel giữ
     [hidden]); đây là giới hạn đã ghi nhận — nav desktop và category-nav vẫn dùng
     được khi JS tắt ở viewport rộng. */
(function () {
    'use strict';

    function wireToggle(btnId, panelId, openLabel, closeLabel) {
        var btn = document.getElementById(btnId);
        var panel = document.getElementById(panelId);
        if (!btn || !panel) return null;
        function set(open) {
            panel.hidden = !open;
            btn.setAttribute('aria-expanded', open ? 'true' : 'false');
            if (openLabel && closeLabel) btn.setAttribute('aria-label', open ? closeLabel : openLabel);
        }
        function isOpen() { return !panel.hidden; }
        btn.addEventListener('click', function (e) {
            e.stopPropagation();
            set(!isOpen());
        });
        return { btn: btn, panel: panel, set: set, isOpen: isOpen };
    }

    var menu = wireToggle('menu-toggle', 'mobile-menu', 'Mở menu điều hướng', 'Đóng menu điều hướng');
    var user = wireToggle('user-menu-btn', 'user-menu-panel', null, null);

    // Bấm link/nút trong menu mobile thì đóng menu (điều hướng xong không kẹt panel).
    if (menu) {
        menu.panel.addEventListener('click', function (e) {
            if (e.target.closest('a, button')) menu.set(false);
        });
    }

    // Bấm ra ngoài dropdown tài khoản thì đóng (không áp cho menu mobile vì
    // menu là panel full-width trong header, bấm link đã tự đóng).
    document.addEventListener('click', function (e) {
        if (user && user.isOpen() && !e.target.closest('.user-menu')) user.set(false);
    });

    // Esc đóng cả hai; trả focus về nút đã mở.
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        if (menu && menu.isOpen()) { menu.set(false); menu.btn.focus(); }
        if (user && user.isOpen()) { user.set(false); user.btn.focus(); }
    });
})();
