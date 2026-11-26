(function () {
    'use strict';
    const form = document.getElementById('need-finder');
    if (!form) return;

    const options = Array.from(document.querySelectorAll('template[data-suggestion]'));
    const drawings = Array.from(document.querySelectorAll('[data-drawing]'));
    const original = drawings.find(drawing => drawing.dataset.initial);
    const suggestions = document.querySelector('[data-suggestions]');

    function setNeed(link, need) {
        const url = new URL(link.href);
        if (need) url.searchParams.set('NhuCau', need);
        else url.searchParams.delete('NhuCau');
        link.href = url.href;
    }

    function showChoices() {
        const formData = new FormData(form);
        const need = formData.get('NhuCau');
        const max = Number(formData.get('GiaDen')) || Infinity;
        const matches = options.filter(option =>
            (!need || option.dataset.needs.split(' ').includes(need)) && Number(option.dataset.price) <= max
        );
        const status = document.createElement('p');
        status.textContent = matches.length
            ? matches.length + ' sản phẩm phù hợp trong các gợi ý trên trang'
            : 'Không có sản phẩm phù hợp trong các gợi ý. Thử tăng ngân sách hoặc mở danh sách sản phẩm.';
        const list = document.createElement('ul');
        matches.slice(0, 3).forEach(option => {
            const item = document.createElement('li');
            item.append(option.content.cloneNode(true));
            setNeed(item.querySelector('a'), need);
            list.append(item);
        });
        suggestions.replaceChildren(status);
        if (matches.length) suggestions.append(list);

        const selected = drawings.find(drawing => drawing.dataset.drawing === matches[0]?.dataset.suggestion)
            || (!need && max === Infinity ? original : null);
        drawings.forEach(drawing => {
            const wasHidden = drawing.hidden;
            drawing.hidden = drawing !== selected;
            if (drawing.hidden) drawing.classList.remove('is-drawing', 'is-dropping');
            else {
                setNeed(drawing.querySelector('[data-hero-link]'), need);
                if (wasHidden) drawing.classList.add('is-drawing');
            }
        });
    }

    form.addEventListener('change', showChoices);
    if (form.querySelector('[name="NhuCau"]:checked') || form.querySelector('[name="GiaDen"]:checked')?.value) showChoices();
    else original?.classList.add('is-drawing', 'is-dropping');

    if ('IntersectionObserver' in window) {
        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            });
        });
        document.querySelectorAll('[data-flash-meter]').forEach(meter => observer.observe(meter));
    }
})();
