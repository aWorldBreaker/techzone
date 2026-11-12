/* Nhom D — Bieu do cot doanh thu 30 ngay o Dashboard Admin.
   Doc cac .chart-bar[data-value], chuan hoa theo gia tri lon nhat
   roi set style.height luc chay (tinh theo px kha dung trong cot,
   tru phan nhan ngay ben duoi de cot khong tran khung). Boc try/catch
   de loi JS khong vo trang. */
(function () {
    function veBieuDo() {
        try {
            var bars = document.querySelectorAll('.chart-bar[data-value]');
            if (!bars.length) return;
            var max = 0;
            var values = [];
            bars.forEach(function (bar) {
                var v = parseFloat(bar.getAttribute('data-value')) || 0;
                values.push(v);
                if (v > max) max = v;
            });
            if (max <= 0) return;
            bars.forEach(function (bar, i) {
                var col = bar.parentElement;
                var label = col ? col.querySelector('.chart-label') : null;
                // Dot 2 (A4): tru ca nhan gia tri (.chart-value) tren cot —
                // neu khong cot se doi len tren, tran khung chart.
                var value = col ? col.querySelector('.chart-value') : null;
                var avail = col ? col.clientHeight - (label ? label.offsetHeight : 0) - (value ? value.offsetHeight : 0) : 0;
                if (avail <= 0) return;
                // Cot 0 thi cao 0; cot khac 0 toi thieu 3px de van nhin thay.
                var h = Math.round((values[i] / max) * avail);
                if (values[i] > 0 && h < 3) h = 3;
                bar.style.height = h + 'px';
            });
        } catch (e) {
            /* Bo qua: bieu do khong ve duoc thi bang so lieu van hien. */
        }
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', veBieuDo);
    } else {
        veBieuDo();
    }
})();
