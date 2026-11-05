/* product-3d.js — Viewer 3D dung chung, trang chi tiet (Sprint 5).
   Chay khi co #product-3d; camera-orbit/orientation lay tu attribute
   tren <model-viewer> (do server render tu Product3dModels.Map).
   File GLB chi tai khi user bam "Xem 3D" (src gan lan dau qua
   ảnh sản phẩm + thông báo, không bao giờ để trang trắng. */
(function () {
    var box = document.getElementById('product-3d');
    if (!box) return;

    var viewer = document.getElementById('product-viewer');
    var img = document.getElementById('product-image');
    var errBox = document.getElementById('viewer-error');
    var btnShow = document.getElementById('btn-3d-show');
    var btnHide = document.getElementById('btn-3d-hide');
    var btnReset = document.getElementById('btn-3d-reset');
    var btnRotate = document.getElementById('btn-3d-rotate');
    var btnFull = document.getElementById('btn-3d-fullscreen');
    if (!viewer || !img || !btnShow) return;

    var modelUrl = box.getAttribute('data-model-url') || '';
    var loaded = false;
    // Sprint 5 fix follow-up (Worker D): viewport hep (<480px) thi khung
    // viewer hep hon cao — model rong (VD tai nghe) bi cat 2 ben o radius
    // goc. Noi radius khoi tao +25% chi khi hep; desktop giu nguyen goc
    // da chot trong map. Chi ap luc load (khong theo doi resize sau do);
    // reset dung lai orbit da dieu chinh nay.
    var NARROW_WIDTH = 480, NARROW_RADIUS_BOOST = 1.25;
    function adjustOrbitForViewport(orbit) {
        if (!window.matchMedia || window.innerWidth >= NARROW_WIDTH) return orbit;
        var parts = String(orbit).trim().split(/\s+/);
        if (parts.length < 3) return orbit;
        var m = parts[2].match(/^([\d.]+)%$/);
        if (!m) return orbit;
        parts[2] = (parseFloat(m[1]) * NARROW_RADIUS_BOOST).toFixed(1).replace(/\.0$/, '') + '%';
        return parts.join(' ');
    }
    var initialOrbit = adjustOrbitForViewport(viewer.getAttribute('camera-orbit') || '0deg 75deg 105%');
    if (initialOrbit !== viewer.getAttribute('camera-orbit')) viewer.setAttribute('camera-orbit', initialOrbit);
    var initialTarget = viewer.getAttribute('camera-target') || 'auto auto auto';
    var initialFov = viewer.getAttribute('field-of-view') || '45deg';

    // Ton trong reduced-motion: tat tu xoay ngay tu dau.
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduceMotion) viewer.removeAttribute('auto-rotate');

    function updateRotateBtn() {
        if (!btnRotate) return;
        var on = viewer.hasAttribute('auto-rotate');
        btnRotate.textContent = 'Tự xoay: ' + (on ? 'Bật' : 'Tắt');
        btnRotate.setAttribute('aria-pressed', on ? 'true' : 'false');
    }

    function showError(msg) {
        if (errBox) { errBox.textContent = msg; errBox.hidden = false; }
        showImage();
    }

    function showViewer() {
        if (!window.customElements || !window.customElements.get('model-viewer')) {
            showError('Trình duyệt chưa tải được thư viện xem 3D — đang hiển thị ảnh sản phẩm.');
            return;
        }
        if (!loaded && modelUrl) { viewer.setAttribute('src', modelUrl); loaded = true; }
        img.hidden = true;
        viewer.hidden = false;
        btnShow.hidden = true;
        if (btnHide) btnHide.hidden = false;
        if (btnReset) btnReset.hidden = false;
        if (btnRotate) btnRotate.hidden = false;
        if (btnFull) btnFull.hidden = false;
        updateRotateBtn();
    }

    function showImage() {
        viewer.hidden = true;
        img.hidden = false;
        btnShow.hidden = false;
        if (btnHide) btnHide.hidden = true;
        if (btnReset) btnReset.hidden = true;
        if (btnRotate) btnRotate.hidden = true;
        if (btnFull) btnFull.hidden = true;
        if (errBox) errBox.hidden = true;
    }

    btnShow.addEventListener('click', showViewer);
    if (btnHide) btnHide.addEventListener('click', showImage);
    if (btnReset) btnReset.addEventListener('click', function () {
        try {
            viewer.cameraOrbit = initialOrbit;
            viewer.cameraTarget = initialTarget;
            viewer.fieldOfView = initialFov;
        } catch (e) { /* giữ nguyên góc nhìn hiện tại */ }
    });
    if (btnRotate) btnRotate.addEventListener('click', function () {
        if (viewer.hasAttribute('auto-rotate')) viewer.removeAttribute('auto-rotate');
        else viewer.setAttribute('auto-rotate', '');
        updateRotateBtn();
    });
    if (btnFull) btnFull.addEventListener('click', function () {
        try {
            if (viewer.requestFullscreen) viewer.requestFullscreen();
            else if (box.requestFullscreen) box.requestFullscreen();
        } catch (e) { /* trình duyệt chặn fullscreen */ }
    });
    viewer.addEventListener('error', function () {
        showError('Không tải được mô hình 3D — đang hiển thị ảnh sản phẩm.');
    });

    // "Xem 3D" chỉ bật khi thư viện model-viewer đã đăng ký xong.
    if (window.customElements && window.customElements.get('model-viewer')) {
        btnShow.disabled = false;
    } else if (window.customElements && window.customElements.whenDefined) {
        btnShow.disabled = true;
        btnShow.title = 'Đang tải thư viện xem 3D…';
        window.customElements.whenDefined('model-viewer').then(function () {
            btnShow.disabled = false;
            btnShow.title = '';
        }).catch(function () { /* giữ nút tắt, user vẫn xem được ảnh */ });
    }
    updateRotateBtn();
})();
