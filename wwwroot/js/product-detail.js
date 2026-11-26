/* Sprint 6 Module 4 — trang chi tiet: gallery thumbnail + gia theo bien the.
   - Thumbnail: moi anh la <button class="thumb-btn"> (xem Details.cshtml);
     bam (click/Enter/Space — button native) doi anh chinh #product-image
     + danh dau nut dang chon (aria-current). Khong JS van xem duoc anh
     dau tien (anh chinh mac dinh la AnhDaiDien).
   - 3D: neu viewer dang hien thi quay ve anh truoc roi moi doi (bam nut
     "Xem anh" cua product-3d.js — khong cham logic toolbar), dong thoi
     cap nhat poster cua model-viewer de mo lai 3D van dung anh moi.
   - Bien the: select#variantId co data-gia/data-gia-goc theo tung option
     (controller tinh bang cung TinhGiaAsync nhu khi them gio — DonGia da
     cong GiaThem); doi select cap nhat gia va bao gia tra gop. */
(function () {
    'use strict';

    function dinhDangTien(gia) {
        return Math.round(gia).toLocaleString('vi-VN') + 'đ';
    }

    /* --- Gallery thumbnail --- */
    var anhChinh = document.getElementById('product-image');
    var viewer = document.getElementById('product-viewer');
    var nutXemAnh = document.getElementById('btn-3d-hide');
    var thumbs = document.querySelectorAll('.thumb-btn[data-full]');
    thumbs.forEach(function (nut) {
        nut.addEventListener('click', function () {
            var url = nut.getAttribute('data-full');
            if (!url) return;
            // Dang xem 3D thi ve anh truoc (dung nut co san cua toolbar),
            // tranh doi anh trong luc viewer dang hien.
            if (viewer && !viewer.hidden && nutXemAnh && !nutXemAnh.hidden) nutXemAnh.click();
            if (anhChinh) anhChinh.src = url;
            if (viewer) viewer.setAttribute('poster', url);
            thumbs.forEach(function (t) { t.removeAttribute('aria-current'); });
            nut.setAttribute('aria-current', 'true');
        });
    });

    /* --- Gia theo bien the --- */
    var selectVariant = document.getElementById('variantId');
    var giaLon = document.querySelector('.price-big');
    var giaGach = document.querySelector('.price-old');
    var traGop = document.querySelectorAll('.detail-buy [data-installment]');
    if (!selectVariant || !giaLon) return;

    function capNhatGia() {
        var luaChon = selectVariant.options[selectVariant.selectedIndex];
        if (!luaChon) return;
        var gia = parseFloat(luaChon.getAttribute('data-gia') || '');
        var giaGoc = parseFloat(luaChon.getAttribute('data-gia-goc') || '');
        if (isNaN(gia)) return; // Khong co data-gia (vd render truc tiep khong qua controller): giu gia server.
        giaLon.textContent = dinhDangTien(gia);
        // Chi gach gia goc khi that su duoc sale; khong sale thi an gia
        // gach cu (tranh hien gia goc cua bien the mac dinh lech voi gia moi).
        if (!isNaN(giaGoc) && gia < giaGoc) {
            if (giaGach) {
                giaGach.textContent = dinhDangTien(giaGoc);
                giaGach.hidden = false;
            }
        } else if (giaGach) {
            giaGach.hidden = true;
        }
        traGop.forEach(function (khoan) {
            var soTien = luaChon.getAttribute('data-installment-' + khoan.dataset.installment);
            if (soTien !== null) khoan.textContent = soTien;
        });
    }
    selectVariant.addEventListener('change', capNhatGia);
    // Đồng bộ với phiên bản trình duyệt khôi phục khi quay về trang.
    capNhatGia();
})();
