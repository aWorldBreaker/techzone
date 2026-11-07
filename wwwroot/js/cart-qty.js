/* M5-1 — Gio hang: chan so luong vuot ton kho ngay tren o nhap.
   Input co max=TonKho nhung trinh duyet chi chan submit bang bubble
   tieng Anh, khong bao loi trong trang; nguoi dung go 99 roi bam
   "Cap nhat" thay nhu khong co gi xay ra. JS nay cap gia tri ve max
   khi doi (change) de submit luon chay + tong cap nhat ngay; server
   (CartController.Update) van cap lai + bao TempData neu POST vuot.
   Sprint 6 Module 4 — nut - / + hai ben o so luong: bam thi doi gia
   tri trong o nhap (kep ve [0, max=TonKho] truoc khi submit) roi
   submit form Update hien co (POST, khong doi endpoint). Nhan - o
   so luong 1 ve 0 = xoa dong (CartService.UpdateSoLuong xoa dong khi
   soLuong <= 0; server van kiem lai). Tong tien dong/tong gio cap
   nhat sau redirect (co che hien co). */
(function () {
    'use strict';

    document.querySelectorAll('form.qty').forEach(function (form) {
        var input = form.querySelector('input[name="soLuong"]');
        if (!input) return;
        var max = parseInt(input.getAttribute('max') || '0', 10);
        form.querySelectorAll('.qty-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var delta = parseInt(btn.getAttribute('data-delta') || '0', 10);
                var v = parseInt(input.value, 10);
                if (isNaN(v)) v = 0;
                v += delta;
                if (v < 0) v = 0;
                if (max && v > max) v = max;
                input.value = String(v);
                form.submit();
            });
        });
    });

    document.querySelectorAll('.cart-item input[name="soLuong"]').forEach(function (input) {
        var max = parseInt(input.getAttribute('max') || '0', 10);
        if (!max) return;
        input.addEventListener('change', function () {
            var v = parseInt(input.value, 10);
            if (isNaN(v) || v < 0) { input.value = '0'; return; }
            if (v > max) input.value = String(max);
        });
    });
})();
