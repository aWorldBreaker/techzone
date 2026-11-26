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

/* Bộ lọc và sắp xếp cho mục đang mở trong Trang tổng hợp. */
(function () {
    'use strict';
    var panel = document.querySelector('.tongquan-panel');
    if (!panel) return;
    var activeLink = document.querySelector('.tongquan-tabs [aria-current="page"]');
    if (activeLink) {
        var nav = activeLink.parentElement;
        function hienMucDangMo() {
            if (nav.scrollWidth <= nav.clientWidth) return;
            var khung = nav.getBoundingClientRect();
            var muc = activeLink.getBoundingClientRect();
            if (muc.left < khung.left || muc.right > khung.right) {
                nav.scrollLeft += muc.left - khung.left;
            }
        }
        hienMucDangMo();
        new ResizeObserver(hienMucDangMo).observe(nav);
        if (document.fonts) document.fonts.ready.then(hienMucDangMo);
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
    function dongLoc(panel) {
        return (panel.querySelector('[data-tongquan-records]') || panel).querySelectorAll('tbody tr, .review-item');
    }
    function capNhatDem(panel, input) {
        var rows = dongLoc(panel);
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
            dongLoc(panel).forEach(function (row) {
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
                var alert = main.querySelector('.alert');
                if (alert) alert.remove();
                var noiDung = document.createElement('div');
                noiDung.className = 'admin-main';
                noiDung.append(...main.children);
                body.replaceChildren(noiDung);
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
