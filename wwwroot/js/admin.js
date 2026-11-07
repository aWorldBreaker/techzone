/* TechZone — JS riêng Admin (Phase 2): chỉ toggle sidebar mobile.
   Active state sidebar + breadcrumb tính phía server trong
   _AdminLayout.cshtml nên không cần JS. Không có JS thì sidebar desktop
   vẫn hiện bình thường; ở mobile không có JS thì nút hamburger không mở
   được sidebar — chấp nhận được vì toàn bộ link Admin vẫn reachable qua
   URL trực tiếp, và trang Admin vốn yêu cầu JS cho biểu đồ Dashboard. */
(function () {
    'use strict';
    var shell = document.getElementById('admin-shell');
    var toggle = document.getElementById('admin-menu-toggle');
    var overlay = document.getElementById('admin-overlay');
    if (!shell || !toggle) return;

    function setOpen(open) {
        shell.classList.toggle('sidebar-open', open);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        toggle.setAttribute('aria-label', open ? 'Đóng menu quản trị' : 'Mở menu quản trị');
    }
    toggle.addEventListener('click', function () {
        setOpen(!shell.classList.contains('sidebar-open'));
    });
    if (overlay) overlay.addEventListener('click', function () { setOpen(false); });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') setOpen(false);
    });
    // Bấm 1 link trong sidebar ở mobile thì đóng sidebar lại.
    var sidebar = document.getElementById('admin-sidebar');
    if (sidebar) sidebar.addEventListener('click', function (e) {
        if (e.target.closest('a') && window.matchMedia('(max-width: 768px)').matches) setOpen(false);
    });
})();
