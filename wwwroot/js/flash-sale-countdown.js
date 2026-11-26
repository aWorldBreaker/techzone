(function () {
  'use strict';

  document.querySelectorAll('.flash-countdown[data-ket-thuc]').forEach(function (clock) {
    var deadline = Date.parse(clock.dataset.ketThuc);
    var numbers = clock.querySelectorAll('b');
    if (!Number.isFinite(deadline) || numbers.length !== 3) return;
    var interval;

    function tick() {
      var seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
      if (seconds === 0) {
        clearInterval(interval);
        clock.textContent = clock.dataset.countdownEnded;
        return false;
      }
      var parts = [Math.floor(seconds / 3600), Math.floor(seconds % 3600 / 60), seconds % 60];
      numbers.forEach(function (text, index) {
        text.textContent = String(parts[index]).padStart(2, '0');
      });
      return true;
    }

    if (tick()) interval = setInterval(tick, 1000);
  });
})();
