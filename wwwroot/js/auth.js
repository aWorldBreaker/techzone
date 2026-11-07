/* TechZone — tương tác trang xác thực (Sprint 6 Continuation, Phase 3).
   Progressive enhancement: tắt JS thì form vẫn POST bình thường
   (nút toggle chỉ là type="button", cảnh báo Caps Lock mặc định ẩn,
   nút submit vẫn submit được). Không có JS thì chỉ mất 3 tiện ích
   dưới đây, không mất chức năng đăng nhập/đăng ký. */
(function () {
    'use strict';

    /* 1. Show/hide password: nút .password-toggle nằm cạnh mỗi ô mật khẩu
       (Login: 1 ô; Register: 2 ô). Đổi type password <-> text, cập nhật
       aria-label ("Hiện mật khẩu"/"Ẩn mật khẩu") + aria-pressed. */
    var toggles = document.querySelectorAll('.password-toggle');
    Array.prototype.forEach.call(toggles, function (btn) {
        btn.addEventListener('click', function () {
            var wrap = btn.closest('.password-field');
            var input = wrap ? wrap.querySelector('input') : null;
            if (!input) return;
            var dangAn = input.type === 'password';
            input.type = dangAn ? 'text' : 'password';
            btn.setAttribute('aria-pressed', dangAn ? 'true' : 'false');
            btn.setAttribute('aria-label', dangAn ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
            btn.textContent = dangAn ? 'Ẩn' : 'Hiện';
            input.focus();
        });
    });

    /* 2. Cảnh báo Caps Lock: phần tử .capslock-warning (mặc định ẩn qua
       thuộc tính hidden) hiện khi getModifierState('CapsLock') bật trên
       ô mật khẩu (keydown/keyup), ẩn khi tắt. Áp cho mọi ô mật khẩu. */
    var passwordInputs = document.querySelectorAll('.auth-form input[type="password"]');
    Array.prototype.forEach.call(passwordInputs, function (input) {
        function kiemTraCapsLock(e) {
            /* Cảnh báo nằm trong .form-group, là anh em của
               .password-field (không nằm trong nó) — tìm từ .form-group. */
            var wrap = input.closest('.form-group');
            var canhBao = wrap ? wrap.querySelector('.capslock-warning') : null;
            if (!canhBao) return;
            var bat = e.getModifierState && e.getModifierState('CapsLock');
            canhBao.hidden = !bat;
        }
        input.addEventListener('keydown', kiemTraCapsLock);
        input.addEventListener('keyup', kiemTraCapsLock);
    });

    /* 3. Trạng thái submit: khi submit form auth, disable nút submit +
       đổi nhãn thành "Đang xử lý…" (aria-disabled kèm disabled) để
       chống submit lặp. Submit event đã kích hoạt nên việc disable nút
       trong handler không chặn POST. */
    var forms = document.querySelectorAll('form.auth-form');
    Array.prototype.forEach.call(forms, function (form) {
        form.addEventListener('submit', function () {
            var nut = form.querySelector('button[type="submit"]');
            if (!nut || nut.disabled) return;
            nut.dataset.nhanGoc = nut.textContent;
            nut.disabled = true;
            nut.setAttribute('aria-disabled', 'true');
            nut.textContent = 'Đang xử lý…';
        });
    });
})();
