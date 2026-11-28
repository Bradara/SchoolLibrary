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

        // Close the theme dropdown after a choice is made. The wrapper is .dropend/.dropup, not
        // .dropdown, so find the toggle through the menu rather than a .dropdown ancestor.
        var menu = btn.closest('.dropdown-menu');
        var toggle = menu && menu.parentElement.querySelector('[data-bs-toggle="dropdown"]');
        if (toggle && window.bootstrap) {
            bootstrap.Dropdown.getOrCreateInstance(toggle).hide();
        }
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

// Resource visibility (_AudiencePicker): a preset button ticks exactly its groups
document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-sl-audience-preset]');
    if (!btn) return;

    var picker = btn.closest('.sl-audience');
    var chosen = btn.getAttribute('data-sl-audience-preset').split(',').filter(Boolean);
    picker.querySelectorAll('input[type="checkbox"][name="Audience"]').forEach(function (box) {
        box.checked = chosen.indexOf(box.value) !== -1;
    });
});

// A <select data-navigate-select> whose option has data-href opens that page instead of being chosen
// (the "add resource" form offers the upload form this way). Without JavaScript the option is empty and
// the form asks for a kind.
document.addEventListener('change', function (e) {
    var select = e.target.closest('select[data-navigate-select]');
    if (!select) return;
    var option = select.options[select.selectedIndex];
    var href = option && option.getAttribute('data-href');
    if (href) window.location.href = href;
});

// Coming back with the browser's Back button may restore the navigating option; choose the page's own
// option again (the one the page was drawn with).
window.addEventListener('pageshow', function () {
    document.querySelectorAll('select[data-navigate-select]').forEach(function (select) {
        var option = select.options[select.selectedIndex];
        if (!option || !option.getAttribute('data-href')) return;
        var own = Array.prototype.find.call(select.options, function (o) { return o.defaultSelected; });
        if (own) own.selected = true; else select.selectedIndex = 0;
    });
});
