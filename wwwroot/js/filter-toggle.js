/* TechZone — Sprint 6 Module 3 (M3-2): panel lọc thu gọn ở mobile.
   - Chỉ thu gọn ở ≤768px; desktop form luôn mở, nút toggle ẩn (CSS).
   - Không JS: nút toggle không hiện (class .is-ready chỉ JS thêm),
     form luôn mở — vẫn lọc được bình thường.
   - Có filter đang bật (server render data-active-filters > 0) thì panel
     tự mở để người dùng thấy trạng thái lọc.
   - Esc đóng panel (chỉ khi đang thu gọn ở mobile), trả focus về nút.
   - Không animation khi đóng/mở (đổi [hidden] tức thì) nên không ảnh
     hưởng prefers-reduced-motion.
   - Theo mẫu header-menu.js (Module 1): aria-expanded, Esc, focus. */
(function () {
    'use strict';
    var btn = document.getElementById('filter-toggle');
    var panel = document.getElementById('filter-panel');
    if (!btn || !panel) return;
    var mq = window.matchMedia('(max-width: 768px)');
    var activeFilters = parseInt(panel.getAttribute('data-active-filters') || '0', 10) || 0;
    var userToggled = false;

    function set(open) {
        panel.hidden = !open;
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    function isOpen() { return !panel.hidden; }
    function applyForViewport() {
        if (!mq.matches) { set(true); return; }      // desktop: luôn mở
        if (!userToggled) set(activeFilters > 0);    // mobile lần đầu: mở chỉ khi có filter
    }

    btn.classList.add('is-ready'); // chỉ khi JS chạy nút mới hiện
    btn.addEventListener('click', function () {
        userToggled = true;
        set(!isOpen());
    });
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        if (mq.matches && isOpen()) { userToggled = true; set(false); btn.focus(); }
    });
    if (typeof mq.addEventListener === 'function') {
        mq.addEventListener('change', applyForViewport);
    } else if (typeof mq.addListener === 'function') {
        mq.addListener(applyForViewport);
    }
    applyForViewport();
})();
