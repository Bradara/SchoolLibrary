// Live preview of the subject tile in the admin form: follows the name, the chosen icon and the chosen color.
// Without a choice (a new subject) it shows the look the server will pick for the name.
(function () {
    'use strict';

    document.querySelectorAll('[data-subject-picker]').forEach(function (form) {
        var nameInput = form.querySelector('[data-subject-name]');
        var tile = form.querySelector('[data-subject-preview]');
        var iconEl = form.querySelector('[data-subject-preview-icon]');
        var nameEl = form.querySelector('[data-subject-preview-name]');
        if (!tile || !iconEl || !nameEl) return;

        var looks = {};
        try { looks = JSON.parse(form.getAttribute('data-standard-looks') || '{}'); } catch (e) { looks = {}; }
        var defaultIcon = form.getAttribute('data-default-icon');
        var defaultColor = form.getAttribute('data-default-color');

        function checkedValue(name) {
            var input = form.querySelector('input[name="' + name + '"]:checked');
            return input ? input.value : '';
        }

        function update() {
            var name = nameInput ? nameInput.value.trim() : '';
            var auto = looks[name] || [defaultIcon, defaultColor];
            var icon = checkedValue('Icon') || auto[0];
            var color = checkedValue('Color') || auto[1];

            // Only values that the page itself offered (the radio values) or the server sent
            if (/^bi-[a-z0-9-]+$/.test(icon)) iconEl.className = 'bi ' + icon;
            if (/^#[0-9a-f]{6}$/.test(color)) tile.style.setProperty('--subj', color);
            nameEl.textContent = name || 'Име на предмет';
        }

        form.addEventListener('change', update);
        if (nameInput) nameInput.addEventListener('input', update);
        update();
    });
})();
