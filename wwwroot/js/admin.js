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
        // C6 (Dot 1): Esc dong sidebar thi tra focus ve nut hamburger
        // (truoc day focus roi khoi nut, nguoi dung ban phim mat vi tri).
        if (e.key === 'Escape' && shell.classList.contains('sidebar-open')) {
            setOpen(false);
            toggle.focus();
        }
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

    // Dot 3 (C1): tablist dung chuan WAI-ARIA — roving tabindex (chi tab
    // dang chon tabindex=0, cac tab khac -1), mui ten trai/phai/len/xuong
    // + Home/End doi tab (activation tu dong), focus chuyen sang panel
    // khi doi tab (panel co tabindex="0"). Mui ten khi focus dang o panel
    // cung doi tab (chi khi focus truc tiep tren panel, khong cuop mui
    // ten trong o input/select cua panel).
    function chonTab(key, opts) {
        var focusPanel = opts && opts.focusPanel;
        tabs.forEach(function (tab) {
            var active = tab.getAttribute('data-tongquan-tab') === key;
            tab.classList.toggle('is-active', active);
            tab.setAttribute('aria-selected', active ? 'true' : 'false');
            tab.tabIndex = active ? 0 : -1;
        });
        panels.forEach(function (panel) {
            var active = panel.getAttribute('data-tongquan-panel') === key;
            panel.classList.toggle('is-active', active);
            panel.hidden = !active;
        });
        if (focusPanel) {
            var panel = document.querySelector('[data-tongquan-panel="' + key + '"]');
            if (panel) panel.focus();
        }
        try { sessionStorage.setItem('tongquan-tab', key); } catch (e) { /* bo qua */ }
    }
    function tabKeBen(tab, buoc) {
        var i = Array.prototype.indexOf.call(tabs, tab);
        return tabs[(i + buoc + tabs.length) % tabs.length];
    }
    function kichHoatTab(tab) {
        chonTab(tab.getAttribute('data-tongquan-tab'), { focusPanel: true });
    }
    function xuLyPhimTab(e, tabHienTai) {
        var dich = null;
        if (e.key === 'ArrowRight' || e.key === 'ArrowDown') dich = tabKeBen(tabHienTai, 1);
        else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') dich = tabKeBen(tabHienTai, -1);
        else if (e.key === 'Home') dich = tabs[0];
        else if (e.key === 'End') dich = tabs[tabs.length - 1];
        if (dich) { e.preventDefault(); dich.focus(); kichHoatTab(dich); }
    }
    tabs.forEach(function (tab) {
        tab.addEventListener('click', function () {
            chonTab(tab.getAttribute('data-tongquan-tab'), { focusPanel: true });
        });
        tab.addEventListener('keydown', function (e) { xuLyPhimTab(e, tab); });
    });
    panels.forEach(function (panel) {
        panel.addEventListener('keydown', function (e) {
            if (e.target !== panel) return;
            var hien = document.querySelector('[data-tongquan-tab].is-active');
            if (hien) xuLyPhimTab(e, hien);
        });
    });
    // Giu tab dang xem khi quay lai trang sau 1 thao tac POST.
    var tabDaLuu = null;
    try { tabDaLuu = sessionStorage.getItem('tongquan-tab'); } catch (e) { /* bo qua */ }
    // C6 (Dot 1): panel KHONG con `hidden` cung trong markup (no-JS van xem
    // du 12/12 panel) — JS phai an cac panel khong active ngay khi load,
    // ke ca khi chua co tab luu (mac dinh ve tab dashboard).
    if (tabDaLuu && document.querySelector('[data-tongquan-tab="' + tabDaLuu + '"]')) {
        chonTab(tabDaLuu);
    } else {
        chonTab('dashboard');
    }

    // Loc client-side: input[data-tongquan-filter="<id panel>"] loc cac
    // dong <tr> (bang) va .review-item trong panel do theo chu thuong.
    // TQ-01: loai text trong <select> cua dong ra khoi chuoi so khop —
    // moi dong don hang deu chua <select> doi trang thai voi du moi
    // option nen row.textContent dong nao cung chua moi chuoi trang thai
    // (loc "Cho xac nhan" tra ve ca 5 dong). O trang thai HIEN THI
    // (o <td>) van giu lai nen loc theo trang thai hoat dong dung.
    // Dot 2 (B7): sau moi lan loc, cap nhat dem "N ket qua" (aria-live)
    // + empty state khi loc ra 0 dong (co nut "Xoa bo loc").
    function capNhatDem(panel, input) {
        var rows = panel.querySelectorAll('tbody tr, .review-item');
        var hien = 0;
        rows.forEach(function (row) { if (row.style.display !== 'none') hien++; });
        var dem = panel.querySelector('.tongquan-count');
        if (!dem) {
            dem = document.createElement('p');
            dem.className = 'tongquan-count text-muted';
            dem.setAttribute('aria-live', 'polite');
            // Chen ngay sau o loc (hoac sau hang loc/sort neu co).
            var neo = input.closest('.tongquan-filter-row') || input;
            neo.parentNode.insertBefore(dem, neo.nextSibling);
        }
        dem.textContent = hien + ' kết quả';
        var rong = panel.querySelector('.tongquan-empty');
        if (input.value.trim() !== '' && hien === 0) {
            if (!rong) {
                rong = document.createElement('div');
                rong.className = 'tongquan-empty';
                rong.setAttribute('role', 'status');
                var p = document.createElement('p');
                rong.appendChild(p);
                var nut = document.createElement('button');
                nut.type = 'button';
                nut.className = 'btn btn-secondary btn-sm tongquan-clear-filter';
                nut.textContent = 'Xóa bộ lọc';
                nut.addEventListener('click', function () {
                    input.value = '';
                    input.dispatchEvent(new Event('input'));
                    input.focus();
                });
                rong.appendChild(nut);
                dem.parentNode.insertBefore(rong, dem.nextSibling);
            }
            rong.hidden = false;
            rong.querySelector('p').textContent = '🔍 Không tìm thấy kết quả nào phù hợp với “' + input.value.trim() + '”. Thử từ khóa khác hoặc xóa bộ lọc.';
        } else if (rong) {
            rong.hidden = true;
        }
    }
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
            capNhatDem(panel, input);
        });
        // Dem ban dau (chua loc): so dong hien co cua panel.
        var panelBanDau = document.getElementById(input.getAttribute('data-tongquan-filter'));
        if (panelBanDau) capNhatDem(panelBanDau, input);
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

/* Dot 3 (B4): Drawer xem nhanh don (read-only).
   Nut [data-order-drawer="<id>"] o Orders / TongQuan-tab-Don mo drawer:
   fetch action OrderDetails() da co, trich .admin-main, BO cac <form>
   (doi trang thai / cap lai ma) de drawer chi doc — xem day du thi dung
   link o chan drawer. Focus trap + Esc + tra focus ve nut da bam
   (chua co mau modal trong code Admin — lam theo WAI-ARIA dialog).
   Khong co JS thi nut van la link thuong? Khong — nut type=button nen
   no-JS khong mo duoc drawer, nhung link "Xem / doi trang thai" ben canh
   van di toi trang day du (khong mat chuc nang). */
(function () {
    'use strict';
    var drawer = document.getElementById('order-drawer');
    var overlay = document.getElementById('order-drawer-overlay');
    var body = document.getElementById('order-drawer-body');
    var tieuDe = document.getElementById('order-drawer-title');
    var nutDong = document.getElementById('order-drawer-close');
    var linkDayDu = document.getElementById('order-drawer-full');
    if (!drawer || !overlay || !body || !nutDong) return;
    var nutDaBam = null;

    function layFocusDuoc() {
        return Array.prototype.slice.call(drawer.querySelectorAll('a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])'))
            .filter(function (el) { return el.offsetParent !== null; });
    }
    function moDrawer(id, url, nut) {
        nutDaBam = nut || null;
        tieuDe.textContent = 'Đơn hàng #' + id;
        if (linkDayDu) linkDayDu.href = url;
        body.innerHTML = '<p class="text-muted" role="status">Đang tải đơn #' + id + '…</p>';
        drawer.hidden = false;
        overlay.hidden = false;
        document.body.style.overflow = 'hidden';
        nutDong.focus();
        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); })
            .then(function (html) {
                var doc = new DOMParser().parseFromString(html, 'text/html');
                var main = doc.querySelector('.admin-main');
                if (!main) throw new Error('Khong thay noi dung don');
                // Read-only: bo form doi trang thai / cap lai ma + thong
                // bao TempData + tieu de (da co o head drawer).
                main.querySelectorAll('form').forEach(function (f) { f.remove(); });
                var h1 = main.querySelector('h1');
                if (h1) h1.remove();
                var eyebrow = main.querySelector('.admin-eyebrow');
                if (eyebrow) eyebrow.remove();
                var alert = main.querySelector('.alert');
                if (alert) alert.remove();
                body.innerHTML = '';
                body.appendChild(document.importNode(main, true));
            })
            .catch(function () {
                body.innerHTML = '<p class="alert alert-error" role="alert">Không tải được đơn #' + id + '. Mở <a href="' + url + '">trang đầy đủ</a>.</p>';
            });
    }
    function dongDrawer() {
        drawer.hidden = true;
        overlay.hidden = true;
        document.body.style.overflow = '';
        if (nutDaBam) nutDaBam.focus();
        nutDaBam = null;
    }
    document.addEventListener('click', function (e) {
        var nut = e.target.closest('[data-order-drawer]');
        if (!nut) return;
        e.preventDefault();
        var id = nut.getAttribute('data-order-drawer');
        moDrawer(id, nut.getAttribute('data-order-url') || ('/Admin/OrderDetails/' + id), nut);
    });
    nutDong.addEventListener('click', dongDrawer);
    overlay.addEventListener('click', dongDrawer);
    document.addEventListener('keydown', function (e) {
        if (drawer.hidden) return;
        if (e.key === 'Escape') { e.preventDefault(); dongDrawer(); return; }
        if (e.key !== 'Tab') return;
        // Focus trap: Tab vong trong drawer.
        var els = layFocusDuoc();
        if (!els.length) return;
        var dau = els[0], cuoi = els[els.length - 1];
        if (e.shiftKey && document.activeElement === dau) { e.preventDefault(); cuoi.focus(); }
        else if (!e.shiftKey && document.activeElement === cuoi) { e.preventDefault(); dau.focus(); }
    });
})();
