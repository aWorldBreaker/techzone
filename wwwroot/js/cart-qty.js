/* M5-1 — Gio hang: chan so luong vuot ton kho ngay tren o nhap.
   Input co max=TonKho nhung trinh duyet chi chan submit bang bubble
   tieng Anh, khong bao loi trong trang; nguoi dung go 99 roi bam
   "Cap nhat" thay nhu khong co gi xay ra. JS nay cap gia tri ve max
   khi doi (change) de submit luon chay + tong cap nhat ngay; server
   (CartController.Update) van cap lai + bao TempData neu POST vuot. */
(function () {
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
