// TechZone — Nhom B: dem nguoc toi khung Flash Sale tiep theo.
// Doc gio bat dau tu data-gio-tiep-theo cua #slot-countdown (gio trong ngay 0-23).
// Neu gio do hom nay da qua thi dem toi cung gio ngay mai.
(function () {
  'use strict';

  function pad(n) {
    return String(n).padStart(2, '0');
  }

  function tick() {
    var el = document.getElementById('slot-countdown');
    if (!el) return;
    var gio = parseInt(el.getAttribute('data-gio-tiep-theo'), 10);
    if (isNaN(gio)) return;

    var dich = new Date();
    dich.setHours(gio, 0, 0, 0);
    var bayGio = new Date();
    if (dich <= bayGio) dich.setDate(dich.getDate() + 1);

    var chenh = Math.max(0, Math.floor((dich - bayGio) / 1000));
    var g = Math.floor(chenh / 3600);
    var p = Math.floor((chenh % 3600) / 60);
    var s = chenh % 60;

    var oGio = document.getElementById('slot-cd-gio');
    var oPhut = document.getElementById('slot-cd-phut');
    var oGiay = document.getElementById('slot-cd-giay');
    if (oGio) oGio.textContent = pad(g);
    if (oPhut) oPhut.textContent = pad(p);
    if (oGiay) oGiay.textContent = pad(s);
  }

  document.addEventListener('DOMContentLoaded', function () {
    tick();
    setInterval(tick, 1000);
  });
})();
