(function () {
    'use strict';

    const panel = document.querySelector('[data-annotation-panel]');
    const offer = document.querySelector('[data-detail-offer]');
    const needForm = panel.querySelector('[data-annotation-needs]');
    const specItems = panel.querySelectorAll('[data-spec-kind]');
    const nav = document.querySelector('[data-detail-nav]');
    const links = Array.from(nav.querySelectorAll('a'));
    const sections = links.map(link => document.getElementById(link.hash.slice(1)));

    if (needForm) {
        needForm.addEventListener('submit', event => {
            const choice = event.submitter;
            if (!choice) return;
            event.preventDefault();
            const fields = (choice.dataset.fields || '').split(' ');
            specItems.forEach(item => item.classList.toggle('spec-highlight', fields.includes(item.dataset.specKind)));
            Array.from(needForm.elements).forEach(button => {
                button.classList.toggle('chip-active', button === choice);
                button.setAttribute('aria-pressed', String(button === choice));
            });
            const url = new URL(location.href);
            if (choice.value) url.searchParams.set('NhuCau', choice.value);
            else url.searchParams.delete('NhuCau');
            history.replaceState(history.state, '', url);
        });
        panel.addEventListener('click', event => {
            const row = event.target.closest('.spec-note');
            const link = row?.querySelector('[data-spec-target]');
            const zone = link && document.getElementById(link.hash.slice(1));
            if (!zone) return;
            event.preventDefault();
            panel.querySelectorAll('.spec-located').forEach(item => item.classList.remove('spec-located'));
            const imageButton = document.getElementById('btn-3d-hide');
            if (imageButton && !imageButton.hidden) imageButton.click();
            row.classList.add('spec-located');
            zone.classList.add('spec-located');
        });
    }

    const select = document.getElementById('variantId');
    const price = offer.querySelector('.price-big');
    const discount = offer.querySelector('[data-detail-discount]');
    const mobileBar = document.querySelector('[data-mobile-buy]');
    const mobilePrice = mobileBar?.querySelector('[data-mobile-price]');

    if (select) {
        const choices = document.createElement('div');
        choices.className = 'choice-row variant-choices';
        choices.setAttribute('role', 'group');
        choices.setAttribute('aria-label', 'Phiên bản');
        const buttons = Array.from(select.options, option => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'chip';
            button.textContent = option.text;
            button.addEventListener('click', () => {
                select.value = option.value;
                select.dispatchEvent(new Event('change', { bubbles: true }));
            });
            choices.append(button);
            return button;
        });

        function syncVariant() {
            buttons.forEach((button, index) => {
                const active = index === select.selectedIndex;
                button.classList.toggle('chip-active', active);
                button.setAttribute('aria-pressed', String(active));
            });
            if (mobilePrice) mobilePrice.textContent = price.textContent;
            const option = select.selectedOptions[0];
            if (!option?.dataset.gia || !option.dataset.giaGoc) return;
            const current = Number(option.dataset.gia);
            const original = Number(option.dataset.giaGoc);
            const percent = original > 0 ? Math.round((1 - current / original) * 100) : 0;
            discount.hidden = percent <= 0;
            discount.textContent = '−' + percent + '%';
        }
        select.after(choices);
        select.hidden = true;
        const label = select.labels[0];
        label.id = 'variant-label';
        label.removeAttribute('for');
        choices.setAttribute('aria-labelledby', 'variant-label');
        choices.removeAttribute('aria-label');
        select.addEventListener('change', syncVariant);
        syncVariant();
    }

    let scrollQueued = false;
    function updateNav() {
        scrollQueued = false;
        let current = 0;
        const line = nav.getBoundingClientRect().bottom + 12;
        sections.forEach((section, index) => {
            if (section.getBoundingClientRect().top <= line) current = index;
        });
        links.forEach((link, index) => {
            if (index === current) link.setAttribute('aria-current', 'location');
            else link.removeAttribute('aria-current');
        });
    }
    function queueNav() {
        if (scrollQueued) return;
        scrollQueued = true;
        requestAnimationFrame(updateNav);
    }
    window.addEventListener('scroll', queueNav, { passive: true });
    window.addEventListener('resize', queueNav);
    updateNav();

    if (!('IntersectionObserver' in window)) return;
    if (panel.querySelector('.spec-overlay')) {
        const drawingObserver = new IntersectionObserver(entries => {
            if (!entries[0].isIntersecting) return;
            panel.classList.add('is-drawing');
            offer.classList.add('is-dropping');
            drawingObserver.disconnect();
        });
        drawingObserver.observe(panel.querySelector('.gallery-stage'));
    }
    if (mobileBar) {
        const submit = document.getElementById('detail-buy-submit');
        const mobileObserver = new IntersectionObserver(entries => {
            mobileBar.hidden = entries[0].isIntersecting;
        });
        mobileObserver.observe(submit);
    }
})();
