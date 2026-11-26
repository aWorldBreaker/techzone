(function () {
  'use strict';

  var motion = window.matchMedia('(prefers-reduced-motion: no-preference)');
  document.querySelectorAll('.flash-countdown[data-ket-thuc]').forEach(function (clock) {
    var deadline = Date.parse(clock.dataset.ketThuc);
    var numbers = clock.querySelectorAll('b');
    if (!Number.isFinite(deadline) || numbers.length !== 3) return;
    var interval;
    var started = false;

    function tick() {
      var seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
      if (seconds === 0) {
        clearInterval(interval);
        clock.textContent = clock.dataset.countdownEnded;
        return false;
      }
      var parts = [Math.floor(seconds / 3600), Math.floor(seconds % 3600 / 60), seconds % 60];
      numbers.forEach(function (text, index) {
        var next = String(parts[index]).padStart(2, '0');
        if (text.textContent === next) return;
        text.textContent = next;
        if (started && motion.matches) {
          text.classList.remove('is-ticking');
          requestAnimationFrame(function () { requestAnimationFrame(function () { text.classList.add('is-ticking'); }); });
        }
      });
      started = true;
      return true;
    }

    if (tick()) interval = setInterval(tick, 1000);
  });
})();
