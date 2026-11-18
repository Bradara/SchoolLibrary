// Renders an uploaded EPUB inside the details page, using epub.js (and JSZip).
// The file is fetched from the Download endpoint of this site, so nothing leaves the server.
// Adds: table of contents, font size, a progress bar, and remembering the reading position and the
// font size in localStorage (per browser). The reader colors follow the site theme.
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

    // The reader follows the site theme by reading the page's own colors
    function readSiteColors() {
        var body = window.getComputedStyle(document.body);
        var root = window.getComputedStyle(document.documentElement);
        var link = root.getPropertyValue("--bs-link-color").trim();
        return { fg: body.color, bg: body.backgroundColor, link: link || body.color };
    }

    // !important so it beats a book's own colors (otherwise black text stays black on a dark theme)
    function readerCss(colors) {
        return "html,body{color:" + colors.fg + " !important;background:" + colors.bg + " !important;}" +
            "body *{color:" + colors.fg + " !important;}" +
            "a,a *{color:" + colors.link + " !important;}";
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
            flow: "paginated"
        });

        // Inject the site colors into every rendered chapter (epub.js "override" only reaches <body>,
        // so a book's own text color would otherwise stay dark on a dark theme)
        function restyle() {
            var colors = readSiteColors();
            area.style.background = colors.bg;
            var css = readerCss(colors);
            rendition.getContents().forEach(function (contents) {
                injectStyle(contents.document, css);
            });
        }

        function applyFont() {
            rendition.themes.fontSize(fontPercent + "%");
        }

        applyFont();
        rendition.on("rendered", restyle);

        // Continue from the last read position, if any
        rendition.display(load(POSITION_KEY, undefined) || undefined)
            .then(restyle);

        watchSiteTheme(restyle);
        wireNavigation(rendition);
        wireFont(rendition, applyFont);
        buildToc(book, rendition);
        wireProgress(book, rendition);

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
        document.addEventListener("keyup", function (e) {
            if (e.key === "ArrowLeft") { rendition.prev(); }
            if (e.key === "ArrowRight") { rendition.next(); }
        });
    }

    function wireFont(rendition, applyFont) {
        var dec = el("sl-epub-font-dec");
        var inc = el("sl-epub-font-inc");
        function change(delta) {
            fontPercent = Math.min(FONT_MAX, Math.max(FONT_MIN, fontPercent + delta));
            applyFont();
            save(FONT_KEY, String(fontPercent));
        }
        if (dec) { dec.addEventListener("click", function () { change(-FONT_STEP); }); }
        if (inc) { inc.addEventListener("click", function () { change(FONT_STEP); }); }
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

    function wireProgress(book, rendition) {
        var range = el("sl-epub-range");
        var percent = el("sl-epub-percent");

        function update(cfi) {
            if (!book.locations || !book.locations.length()) {
                return;
            }
            var ratio = book.locations.percentageFromCfi(cfi);
            var value = Math.round((ratio || 0) * 100);
            if (percent) { percent.textContent = value + "%"; }
            if (range) { range.value = value; }
        }

        rendition.on("relocated", function (location) {
            save(POSITION_KEY, location.start.cfi);
            update(location.start.cfi);
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
                    update(current.start.cfi);
                }
            })
            .catch(function () { /* progress bar stays at 0 if locations cannot be built */ });
    }
})();
