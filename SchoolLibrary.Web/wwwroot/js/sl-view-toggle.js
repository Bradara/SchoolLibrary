// Cards or a list for the catalog. The choice is kept in localStorage; the early script in the layout applies it
// (data-resource-view on <html>) before the page is drawn, so the cards never flash. Without storage the
// toggle still works for the page, it is only not remembered.
(function () {
    'use strict';

    var STORAGE_KEY = 'resource-view';
    var root = document.documentElement;
    var toggles = document.querySelectorAll('[data-view-toggle]');
    if (!toggles.length) return;

    function current() {
        return root.getAttribute('data-resource-view') === 'list' ? 'list' : 'cards';
    }

    function sync() {
        var view = current();
        toggles.forEach(function (group) {
            group.querySelectorAll('[data-view]').forEach(function (button) {
                var pressed = button.getAttribute('data-view') === view;
                button.setAttribute('aria-pressed', pressed ? 'true' : 'false');
                button.classList.toggle('active', pressed);
            });
        });
    }

    function choose(view) {
        if (view === 'list') {
            root.setAttribute('data-resource-view', 'list');
        } else {
            root.removeAttribute('data-resource-view');
        }

        try {
            localStorage.setItem(STORAGE_KEY, view);
        } catch (e) { }

        sync();
    }

    toggles.forEach(function (group) {
        group.hidden = false;
        group.addEventListener('click', function (event) {
            var button = event.target.closest('[data-view]');
            if (button && group.contains(button)) {
                choose(button.getAttribute('data-view') === 'list' ? 'list' : 'cards');
            }
        });
    });

    sync();
})();
