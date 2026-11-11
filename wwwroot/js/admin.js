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

/* Trang tong hop (Admin/TongQuan): chuyen tab + loc/sort client-side.
   Generic theo data- attribute; khong cham toi toggle sidebar o tren. */
(function () {
    'use strict';
    var tabs = document.querySelectorAll('[data-tongquan-tab]');
    if (!tabs.length) return;
    var panels = document.querySelectorAll('[data-tongquan-panel]');

    function chonTab(key) {
        tabs.forEach(function (tab) {
            var active = tab.getAttribute('data-tongquan-tab') === key;
            tab.classList.toggle('is-active', active);
            tab.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        panels.forEach(function (panel) {
            var active = panel.getAttribute('data-tongquan-panel') === key;
            panel.classList.toggle('is-active', active);
            panel.hidden = !active;
        });
        try { sessionStorage.setItem('tongquan-tab', key); } catch (e) { /* bo qua */ }
    }
    tabs.forEach(function (tab) {
        tab.addEventListener('click', function () {
            chonTab(tab.getAttribute('data-tongquan-tab'));
        });
    });
    // Giu tab dang xem khi quay lai trang sau 1 thao tac POST.
    var tabDaLuu = null;
    try { tabDaLuu = sessionStorage.getItem('tongquan-tab'); } catch (e) { /* bo qua */ }
    if (tabDaLuu && document.querySelector('[data-tongquan-tab="' + tabDaLuu + '"]')) {
        chonTab(tabDaLuu);
    }

    // Loc client-side: input[data-tongquan-filter="<id panel>"] loc cac
    // dong <tr> (bang) va .review-item trong panel do theo chu thuong.
    // TQ-01: loai text trong <select> cua dong ra khoi chuoi so khop —
    // moi dong don hang deu chua <select> doi trang thai voi du moi
    // option nen row.textContent dong nao cung chua moi chuoi trang thai
    // (loc "Cho xac nhan" tra ve ca 5 dong). O trang thai HIEN THI
    // (o <td>) van giu lai nen loc theo trang thai hoat dong dung.
    document.querySelectorAll('[data-tongquan-filter]').forEach(function (input) {
        input.addEventListener('input', function () {
            var panel = document.getElementById(input.getAttribute('data-tongquan-filter'));
            if (!panel) return;
            var tuKhoa = input.value.trim().toLowerCase();
            panel.querySelectorAll('tbody tr, .review-item').forEach(function (row) {
                var text;
                if (row.querySelector('select')) {
                    var banSao = row.cloneNode(true);
                    banSao.querySelectorAll('select').forEach(function (s) { s.remove(); });
                    text = banSao.textContent.toLowerCase();
                } else {
                    text = row.textContent.toLowerCase();
                }
                row.style.display = !tuKhoa || text.indexOf(tuKhoa) !== -1 ? '' : 'none';
            });
        });
    });

    // Sort client-side: select[data-tongquan-sort="<id panel>"], value
    // "<key>-asc|desc" sap xep cac <tr> theo data-sort-<key> cua dong.
    document.querySelectorAll('[data-tongquan-sort]').forEach(function (select) {
        select.addEventListener('change', function () {
            var panel = document.getElementById(select.getAttribute('data-tongquan-sort'));
            if (!panel || !select.value) return;
            var parts = select.value.split('-');
            var key = parts[0];
            var tang = parts[1] === 'asc';
            panel.querySelectorAll('tbody').forEach(function (tbody) {
                var rows = Array.prototype.slice.call(tbody.querySelectorAll('tr'));
                rows.sort(function (a, b) {
                    var va = a.dataset['sort' + key.charAt(0).toUpperCase() + key.slice(1)] || '';
                    var vb = b.dataset['sort' + key.charAt(0).toUpperCase() + key.slice(1)] || '';
                    var na = parseFloat(va);
                    var nb = parseFloat(vb);
                    var cmp;
                    if (!isNaN(na) && !isNaN(nb) && va !== '' && vb !== '') cmp = na - nb;
                    else cmp = va.localeCompare(vb, 'vi');
                    return tang ? cmp : -cmp;
                });
                rows.forEach(function (row) { tbody.appendChild(row); });
            });
        });
    });
})();
