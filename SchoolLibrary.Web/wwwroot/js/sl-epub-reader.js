// Renders an uploaded EPUB inside the details page, using epub.js (and JSZip).
// The file is fetched from the Download endpoint of this site, so nothing leaves the server.
// Adds: table of contents, font size, text settings (justify, hyphenation, font, line height, two pages),
// a progress bar and remembering the reading position. A signed-in reader's position is kept on the server,
// so the book continues where it was left on any device; a visitor's position stays in this browser.
// The reader colors follow the site theme.
(function () {
    "use strict";

    var area = document.getElementById("sl-epub");
    if (!area || typeof ePub === "undefined") {
        return;
    }

    var url = area.getAttribute("data-epub-url");
    if (!url) {
        return;
    }

    // --- small helpers around localStorage (may be blocked or throw in private mode) ---
    var POSITION_KEY = area.getAttribute("data-epub-key") || "epub-pos";
    var FONT_KEY = "sl-epub-font";
    var SETTINGS_KEY = "sl-epub-settings";

    function load(key, fallback) {
        try {
            var value = localStorage.getItem(key);
            return value === null ? fallback : value;
        } catch (e) {
            return fallback;
        }
    }

    function save(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch (e) {
            // ignore: reading still works without a saved value
        }
    }

    function el(id) {
        return document.getElementById(id);
    }

    // --- text settings (kept per browser: a phone and a computer usually want different ones) ---
    var DEFAULT_SETTINGS = { justify: false, hyphens: false, spread: true, family: "", line: "" };

    var FAMILIES = {
        serif: "Georgia, \"Times New Roman\", serif",
        sans: "system-ui, \"Segoe UI\", Roboto, Arial, sans-serif",
        wide: "Verdana, \"DejaVu Sans\", Tahoma, sans-serif"
    };
    var LINE_HEIGHTS = ["1.3", "1.6", "2"];

    function loadSettings() {
        var settings = {};
        var stored = {};
        try {
            stored = JSON.parse(load(SETTINGS_KEY, "{}")) || {};
        } catch (e) {
            stored = {};
        }
        Object.keys(DEFAULT_SETTINGS).forEach(function (name) {
            settings[name] = typeof stored[name] === typeof DEFAULT_SETTINGS[name] ? stored[name] : DEFAULT_SETTINGS[name];
        });
        // Only known values reach the CSS
        if (!FAMILIES.hasOwnProperty(settings.family)) { settings.family = ""; }
        if (LINE_HEIGHTS.indexOf(settings.line) < 0) { settings.line = ""; }
        return settings;
    }

    var settings = loadSettings();

    // The reader follows the site theme by reading the page's own colors
    function readSiteColors() {
        var body = window.getComputedStyle(document.body);
        var root = window.getComputedStyle(document.documentElement);
        var link = root.getPropertyValue("--bs-link-color").trim();
        return { fg: body.color, bg: body.backgroundColor, link: link || body.color };
    }

    // !important so it beats a book's own colors (otherwise black text stays black on a dark theme)
    function colorCss(colors) {
        return "html,body{color:" + colors.fg + " !important;background:" + colors.bg + " !important;}" +
            "body *{color:" + colors.fg + " !important;}" +
            "a,a *{color:" + colors.link + " !important;}";
    }

    var TEXT_ELEMENTS = "body,p,li,dd,dt,div,span,a,em,strong,i,b,blockquote,td,th,h1,h2,h3,h4,h5,h6";

    function typographyCss() {
        var css = "";
        // Only the paragraphs that were aligned to the left: centered titles and right-aligned lines stay as they are
        if (settings.justify) {
            css += "[data-sl-flow]{text-align:justify !important;}";
        }
        if (settings.hyphens) {
            css += "html,body,[data-sl-flow]{-webkit-hyphens:auto !important;hyphens:auto !important;}";
        }
        if (settings.family) {
            css += TEXT_ELEMENTS + "{font-family:" + FAMILIES[settings.family] + " !important;}";
        }
        if (settings.line) {
            css += TEXT_ELEMENTS + "{line-height:" + settings.line + " !important;}";
        }
        return css;
    }

    function injectStyle(doc, css) {
        if (!doc) {
            return;
        }
        try {
            var style = doc.getElementById("sl-reader-theme");
            if (!style) {
                style = doc.createElement("style");
                style.id = "sl-reader-theme";
                (doc.head || doc.documentElement).appendChild(style);
            }
            style.textContent = css;
        } catch (e) {
            // the chapter document may not be reachable yet; ignore
        }
    }

    // Runs once per chapter, before the reader's own styles: marks the left-aligned blocks (so justify does
    // not touch centered text) and makes sure the chapter has a language, which hyphenation needs
    function prepareChapter(doc, language) {
        if (!doc || !doc.documentElement) {
            return;
        }
        try {
            var html = doc.documentElement;
            var lang = html.getAttribute("lang") || html.getAttribute("xml:lang") ||
                (doc.body && doc.body.getAttribute("lang")) || language || "bg";
            html.setAttribute("lang", lang);

            var view = doc.defaultView;
            Array.prototype.forEach.call(doc.querySelectorAll("p,li,dd,blockquote"), function (block) {
                var align = view.getComputedStyle(block).textAlign;
                if (align === "start" || align === "left" || align === "justify" || align === "auto") {
                    block.setAttribute("data-sl-flow", "");
                }
            });
        } catch (e) {
            // a chapter that cannot be prepared is simply shown as the book has it
        }
    }

    function showError() {
        area.innerHTML =
            '<div class="alert alert-warning m-2">Неуспешно зареждане на книгата. Използвайте бутона за сваляне.</div>';
    }

    var FONT_MIN = 80;
    var FONT_MAX = 180;
    var FONT_STEP = 10;
    var fontPercent = parseInt(load(FONT_KEY, "100"), 10);
    if (isNaN(fontPercent)) {
        fontPercent = 100;
    }

    fetch(url)
        .then(function (response) {
            if (!response.ok) {
                throw new Error("HTTP " + response.status);
            }
            return response.arrayBuffer();
        })
        .then(function (buffer) {
            start(ePub(buffer));
        })
        .catch(showError);

    function start(book) {
        var rendition = book.renderTo("sl-epub", {
            width: "100%",
            height: "100%",
            flow: "paginated",
            spread: settings.spread ? "auto" : "none"
        });

        var language = "";
        book.loaded.metadata.then(function (meta) {
            language = (meta && meta.language) || "";
        }).catch(function () { /* no metadata: Bulgarian is assumed */ });

        function css() {
            return colorCss(readSiteColors()) + typographyCss();
        }

        // Inject the site colors and the text settings into every rendered chapter (epub.js "override" only
        // reaches <body>, so a book's own text color would otherwise stay dark on a dark theme)
        function restyle() {
            area.style.background = readSiteColors().bg;
            var text = css();
            rendition.getContents().forEach(function (contents) {
                injectStyle(contents.document, text);
            });
        }

        rendition.hooks.content.register(function (contents) {
            prepareChapter(contents.document, language);
            injectStyle(contents.document, css());
        });

        function applyFont() {
            rendition.themes.fontSize(fontPercent + "%");
        }

        // A change of the layout moves the text: return to the same place afterwards
        function keepPlace(change) {
            var current = rendition.currentLocation();
            var cfi = current && current.start ? current.start.cfi : null;
            change();
            if (cfi) {
                rendition.display(cfi);
            }
        }

        applyFont();
        rendition.on("rendered", restyle);

        var sync = createSync(book);

        // Continue from the last read position, if any
        rendition.display(sync.startCfi() || undefined)
            .then(restyle)
            .catch(function () {
                // a saved position from another version of the book: start from the beginning
                return rendition.display().then(restyle);
            });

        watchSiteTheme(restyle);
        wireNavigation(rendition);
        wireFont(applyFont, keepPlace);
        wireSettings(rendition, restyle, keepPlace);
        buildToc(book, rendition);
        wireProgress(book, rendition, sync);

        // Reflow when the container changes size (e.g. the maximize button)
        var resizeTimer = null;
        window.addEventListener("resize", function () {
            window.clearTimeout(resizeTimer);
            resizeTimer = window.setTimeout(function () {
                try {
                    rendition.resize();
                } catch (e) {
                    // ignore transient resize errors
                }
            }, 150);
        });
    }

    // --- the reading position: on the server for a signed-in user, in this browser for a visitor ---
    function createSync(book) {
        var saveUrl = area.getAttribute("data-epub-save-url");
        var serverCfi = area.getAttribute("data-epub-position");
        var lastPercent = parseInt(area.getAttribute("data-epub-percent") || "0", 10) || 0;
        var status = el("sl-epub-sync");
        var tokenInput = document.querySelector('#sl-epub-token input[name="__RequestVerificationToken"]');

        var pending = null;
        var savedCfi = serverCfi;
        var timer = null;
        var SAVE_DELAY = 1500;

        function showStatus(ok) {
            if (!status) {
                return;
            }
            status.textContent = "";
            var icon = document.createElement("i");
            icon.className = ok ? "bi bi-cloud-check" : "bi bi-cloud-slash text-warning";
            status.title = ok
                ? "Докъдето сте стигнали, се пази в профила ви — продължете от всяко устройство."
                : "Позицията не можа да се запази в профила ви. Ще опитаме отново.";
            status.appendChild(icon);
        }

        function send(keepalive) {
            window.clearTimeout(timer);
            if (!pending || !saveUrl || !tokenInput || pending.cfi === savedCfi) {
                return;
            }
            var position = pending;
            var body = new FormData();
            body.append("cfi", position.cfi);
            body.append("percent", String(position.percent));
            body.append("__RequestVerificationToken", tokenInput.value);

            fetch(saveUrl, { method: "POST", body: body, credentials: "same-origin", keepalive: !!keepalive })
                .then(function (response) {
                    if (response.ok) {
                        savedCfi = position.cfi;
                        if (pending === position) {
                            pending = null;
                        }
                    }
                    showStatus(response.ok);
                })
                .catch(function () { showStatus(false); });
        }

        if (saveUrl) {
            showStatus(true);
            // Leaving the page (or switching to another app on a phone) saves at once
            document.addEventListener("visibilitychange", function () {
                if (document.visibilityState === "hidden") {
                    send(true);
                }
            });
            window.addEventListener("pagehide", function () { send(true); });
        }

        return {
            // The server position wins (it may come from another device); an older position from this
            // browser is used once and then moved to the server
            startCfi: function () {
                return serverCfi || load(POSITION_KEY, undefined);
            },
            percent: function () {
                return lastPercent;
            },
            moved: function (cfi, percent) {
                if (typeof percent === "number") {
                    lastPercent = percent;
                }
                save(POSITION_KEY, cfi);
                if (saveUrl) {
                    pending = { cfi: cfi, percent: lastPercent };
                    window.clearTimeout(timer);
                    timer = window.setTimeout(function () { send(false); }, SAVE_DELAY);
                }
            }
        };
    }

    // Re-apply the colors whenever the site theme attribute on <html> changes
    function watchSiteTheme(onChange) {
        if (typeof MutationObserver === "undefined") {
            return;
        }
        var observer = new MutationObserver(onChange);
        observer.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ["data-bs-theme", "data-theme"]
        });
    }

    function wireNavigation(rendition) {
        var prev = el("sl-epub-prev");
        var next = el("sl-epub-next");
        if (prev) {
            prev.addEventListener("click", function () { rendition.prev(); });
        }
        if (next) {
            next.addEventListener("click", function () { rendition.next(); });
        }

        function onKey(e) {
            var target = e.target;
            var tag = target && target.tagName ? target.tagName.toLowerCase() : "";
            // Arrows inside a form field (the settings, the comment box) do not turn pages
            if (tag === "input" || tag === "select" || tag === "textarea" || (target && target.isContentEditable)) {
                return;
            }
            if (e.key === "ArrowLeft") { rendition.prev(); }
            if (e.key === "ArrowRight") { rendition.next(); }
        }

        document.addEventListener("keyup", onKey);
        // Also after a click inside the book (the keys then go to the chapter's frame)
        rendition.on("keyup", onKey);
    }

    function wireFont(applyFont, keepPlace) {
        var dec = el("sl-epub-font-dec");
        var inc = el("sl-epub-font-inc");
        function change(delta) {
            fontPercent = Math.min(FONT_MAX, Math.max(FONT_MIN, fontPercent + delta));
            keepPlace(applyFont);
            save(FONT_KEY, String(fontPercent));
        }
        if (dec) { dec.addEventListener("click", function () { change(-FONT_STEP); }); }
        if (inc) { inc.addEventListener("click", function () { change(FONT_STEP); }); }
    }

    function wireSettings(rendition, restyle, keepPlace) {
        var controls = {
            justify: el("sl-epub-justify"),
            hyphens: el("sl-epub-hyphens"),
            spread: el("sl-epub-spread"),
            family: el("sl-epub-family"),
            line: el("sl-epub-line")
        };

        function show() {
            Object.keys(controls).forEach(function (name) {
                var control = controls[name];
                if (!control) {
                    return;
                }
                if (control.type === "checkbox") {
                    control.checked = settings[name];
                } else {
                    control.value = settings[name];
                }
            });
        }

        function apply() {
            save(SETTINGS_KEY, JSON.stringify(settings));
            keepPlace(function () {
                rendition.spread(settings.spread ? "auto" : "none");
                restyle();
            });
        }

        Object.keys(controls).forEach(function (name) {
            var control = controls[name];
            if (!control) {
                return;
            }
            control.addEventListener("change", function () {
                settings[name] = control.type === "checkbox" ? control.checked : control.value;
                apply();
            });
        });

        var reset = el("sl-epub-settings-reset");
        if (reset) {
            reset.addEventListener("click", function () {
                Object.keys(DEFAULT_SETTINGS).forEach(function (name) {
                    settings[name] = DEFAULT_SETTINGS[name];
                });
                show();
                apply();
            });
        }

        show();
    }

    function buildToc(book, rendition) {
        var list = el("sl-epub-toc");
        if (!list) {
            return;
        }
        book.loaded.navigation.then(function (nav) {
            var toc = (nav && nav.toc) || [];
            if (!toc.length) {
                list.innerHTML = '<li><span class="dropdown-item-text text-body-secondary">Няма съдържание</span></li>';
                return;
            }
            toc.forEach(function (item) {
                addTocItem(list, rendition, item, 0);
                (item.subitems || []).forEach(function (sub) {
                    addTocItem(list, rendition, sub, 1);
                });
            });
        });
    }

    function addTocItem(list, rendition, item, depth) {
        var li = document.createElement("li");
        var a = document.createElement("a");
        a.className = "dropdown-item text-wrap";
        a.href = "#";
        a.textContent = item.label ? item.label.trim() : "(без име)";
        if (depth > 0) {
            a.style.paddingLeft = (1 + depth) + "rem";
        }
        a.addEventListener("click", function (e) {
            e.preventDefault();
            rendition.display(item.href);
        });
        li.appendChild(a);
        list.appendChild(li);
    }

    function wireProgress(book, rendition, sync) {
        var range = el("sl-epub-range");
        var percent = el("sl-epub-percent");

        function show(value) {
            if (percent) { percent.textContent = value + "%"; }
            if (range) { range.value = value; }
        }

        // The percentage of a location, or null while the location map is still being built
        function percentOf(cfi) {
            if (!book.locations || !book.locations.length()) {
                return null;
            }
            return Math.round((book.locations.percentageFromCfi(cfi) || 0) * 100);
        }

        show(sync.percent());

        rendition.on("relocated", function (location) {
            var value = percentOf(location.start.cfi);
            sync.moved(location.start.cfi, value);
            if (value !== null) {
                show(value);
            }
        });

        if (range) {
            range.addEventListener("change", function () {
                if (book.locations && book.locations.length()) {
                    rendition.display(book.locations.cfiFromPercentage(Number(range.value) / 100));
                }
            });
        }

        // Building the location map can take a moment; do it in the background, then show progress
        book.ready
            .then(function () { return book.locations.generate(1200); })
            .then(function () {
                var current = rendition.currentLocation();
                if (current && current.start) {
                    var value = percentOf(current.start.cfi);
                    if (value !== null) {
                        show(value);
                    }
                }
            })
            .catch(function () { /* progress bar stays as it was if locations cannot be built */ });
    }
})();
