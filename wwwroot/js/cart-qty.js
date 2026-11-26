(function () {
    'use strict';

    document.querySelectorAll('.cart-item input[name="soLuong"]').forEach(input => {
        const form = input.closest('form.qty');
        if (!form || input.disabled) return;

        const controls = form.querySelectorAll('.qty-btn');
        const max = Number(input.max);
        const min = Number(input.min);
        const status = document.getElementById(input.getAttribute('aria-describedby'));

        function refresh() {
            const quantity = input.valueAsNumber;
            controls.forEach(button => {
                button.disabled = button.dataset.delta === '1' ? quantity >= max : quantity <= min;
            });
            if (status) status.hidden = !(quantity >= max);
        }

        controls.forEach(button => {
            button.hidden = false;
            button.addEventListener('click', () => {
                const quantity = Number.isFinite(input.valueAsNumber) ? input.valueAsNumber : min;
                input.value = String(Math.min(max, Math.max(min, quantity + Number(button.dataset.delta))));
                refresh();
                form.requestSubmit();
            });
        });
        input.addEventListener('input', refresh);
        input.addEventListener('change', () => {
            if (input.valueAsNumber > max) input.value = String(max);
            if (input.valueAsNumber < min) input.value = String(min);
            refresh();
        });
        refresh();
    });
})();
