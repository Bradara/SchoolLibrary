// Color theme switcher. The saved theme is applied earlier by an inline script in <head>
// (so there is no flash); this code handles clicks and keeps the menu in sync.
(function () {
    var root = document.documentElement;
    var darkThemes = ['dark', 'dracula'];

    function applyTheme(theme) {
        root.setAttribute('data-theme', theme);
        root.setAttribute('data-bs-theme', darkThemes.indexOf(theme) >= 0 ? 'dark' : 'light');
    }

    function markActive(theme) {
        document.querySelectorAll('[data-theme-choice]').forEach(function (btn) {
            var isActive = btn.getAttribute('data-theme-choice') === theme;
            btn.classList.toggle('active', isActive);
            if (isActive) {
                btn.setAttribute('aria-current', 'true');
            } else {
                btn.removeAttribute('aria-current');
            }
        });
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-theme-choice]');
        if (!btn) return;

        var theme = btn.getAttribute('data-theme-choice');
        applyTheme(theme);
        try { localStorage.setItem('theme', theme); } catch (err) { }
        markActive(theme);
    });

    markActive(root.getAttribute('data-theme'));

    // Tooltips for the icon-only sidebar. Uses data-sl-tip (not data-bs-toggle) so the same
    // element can also be a dropdown toggle.
    if (window.bootstrap) {
        document.querySelectorAll('[data-sl-tip]').forEach(function (el) {
            var tip = new bootstrap.Tooltip(el, {
                title: el.getAttribute('data-sl-tip'),
                placement: el.getAttribute('data-sl-placement') || 'right',
                container: 'body',
                trigger: 'hover'
            });
            el.addEventListener('click', function () { tip.hide(); });
        });
    }
})();
