// Renders an uploaded EPUB inside the details page, using epub.js (and JSZip).
// The file is fetched from the Download endpoint of this site, so nothing leaves the server.
// Adds: table of contents, font size, text settings (justify, hyphenation, font, line height, two pages),
// bookmarks, search in the book, a progress bar and remembering the reading position. A signed-in reader's position is kept on the server,
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
        wireBookmarks(book, rendition, sync);
        wireSearch(book, rendition);

        area.dispatchEvent(new CustomEvent("sl-epub-ready", {
            detail: {
                book: book,
                rendition: rendition,
                percent: function () { return sync.percent(); },
                helpers: {
                    el: el,
                    load: load,
                    save: save,
                    createStore: createStore,
                    closeDropdown: closeDropdown,
                    chapterTitle: chapterTitle,
                    positionKey: POSITION_KEY
                }
            }
        }));

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
            if (!pending || !saveUrl || !antiforgeryToken() || pending.cfi === savedCfi) {
                return;
            }
            var position = pending;
            var body = new FormData();
            body.append("cfi", position.cfi);
            body.append("percent", String(position.percent));
            body.append("__RequestVerificationToken", antiforgeryToken());

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

    function antiforgeryToken() {
        var input = document.querySelector('#sl-epub-token input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    function postForm(url, fields) {
        var body = new FormData();
        Object.keys(fields).forEach(function (name) {
            body.append(name, String(fields[name]));
        });
        body.append("__RequestVerificationToken", antiforgeryToken());
        return fetch(url, { method: "POST", body: body, credentials: "same-origin" });
    }

    function closeDropdown(buttonId) {
        var button = el(buttonId);
        if (button && window.bootstrap && window.bootstrap.Dropdown) {
            window.bootstrap.Dropdown.getOrCreateInstance(button).hide();
        }
    }

    // The title of the chapter that holds the location, from the table of contents
    function chapterTitle(book, href) {
        if (!href || !book.navigation) {
            return "";
        }
        var file = href.split("#")[0].split("/").pop();
        var found = "";
        function walk(items) {
            (items || []).forEach(function (item) {
                if (!found && item.href && item.href.split("#")[0].split("/").pop() === file) {
                    found = (item.label || "").trim();
                }
                walk(item.subitems);
            });
        }
        walk(book.navigation.toc);
        return found;
    }

    // The first words of the page on screen
    function pageText(rendition, location) {
        try {
            var start = rendition.getRange(location.start.cfi);
            var range = start.startContainer.ownerDocument.createRange();
            range.setStart(start.startContainer, start.startOffset);
            var end = location.end && location.end.cfi ? rendition.getRange(location.end.cfi) : null;
            if (end && end.startContainer.ownerDocument === start.startContainer.ownerDocument) {
                range.setEnd(end.startContainer, end.startOffset);
            } else {
                range.setEndAfter(start.startContainer);
            }
            return range.toString().replace(/\s+/g, " ").trim().slice(0, 90);
        } catch (e) {
            return "";
        }
    }

    // --- a list of the reader's own items (bookmarks, highlights): on the server for a signed-in user,
    // in this browser for a visitor. The same calls either way, all returning promises. ---
    function createStore(options) {
        var urls = options.urls || {};
        var signedIn = !!(urls.list && urls.add && urls.remove);

        function readLocal() {
            try {
                var list = JSON.parse(load(options.localKey, "[]"));
                return Array.isArray(list) ? list.filter(function (item) {
                    return item && typeof item.cfi === "string" && options.isValid(item);
                }) : [];
            } catch (e) {
                return [];
            }
        }

        function writeLocal(list) {
            save(options.localKey, JSON.stringify(list));
        }

        // The server answers { ok, <resultField> } or { ok: false, message }
        function answer(response, failure) {
            return response.json().catch(function () { return {}; }).then(function (data) {
                if (!response.ok || !data.ok) {
                    throw new Error(data.message || failure);
                }
                return data[options.resultField];
            });
        }

        if (signedIn) {
            return {
                list: function () {
                    return fetch(urls.list, { credentials: "same-origin" }).then(function (r) {
                        if (!r.ok) { throw new Error(options.messages.load); }
                        return r.json();
                    });
                },
                add: function (item) {
                    return postForm(urls.add, item).then(function (r) { return answer(r, options.messages.add); });
                },
                update: function (item, changes) {
                    var fields = {};
                    fields[options.idField] = item.id;
                    Object.keys(changes).forEach(function (name) { fields[name] = changes[name]; });
                    return postForm(urls.update, fields).then(function (r) { return answer(r, options.messages.add); });
                },
                remove: function (item) {
                    var fields = {};
                    fields[options.idField] = item.id;
                    return postForm(urls.remove, fields).then(function (r) {
                        if (!r.ok) { throw new Error(options.messages.remove); }
                    });
                }
            };
        }

        return {
            list: function () {
                return Promise.resolve(readLocal());
            },
            add: function (item) {
                var list = readLocal();
                var existing = list.filter(function (b) { return b.cfi === item.cfi; })[0];
                if (existing) {
                    return Promise.resolve(existing);
                }
                if (list.length >= options.max) {
                    return Promise.reject(new Error(options.messages.limit));
                }
                item.id = Date.now();
                list.push(item);
                writeLocal(list);
                return Promise.resolve(item);
            },
            update: function (item, changes) {
                var updated = null;
                var list = readLocal().map(function (b) {
                    if (b.id === item.id) {
                        Object.keys(changes).forEach(function (name) { b[name] = changes[name]; });
                        updated = b;
                    }
                    return b;
                });
                writeLocal(list);
                return updated ? Promise.resolve(updated) : Promise.reject(new Error(options.messages.add));
            },
            remove: function (item) {
                writeLocal(readLocal().filter(function (b) { return b.id !== item.id; }));
                return Promise.resolve();
            }
        };
    }

    // --- bookmarks ---
    var BOOKMARKS_MAX = 50;

    function createBookmarkStore() {
        return createStore({
            urls: {
                list: area.getAttribute("data-epub-bookmarks-url"),
                add: area.getAttribute("data-epub-bookmark-add-url"),
                remove: area.getAttribute("data-epub-bookmark-delete-url")
            },
            localKey: POSITION_KEY + "-bookmarks",
            idField: "bookmarkId",
            resultField: "bookmark",
            max: BOOKMARKS_MAX,
            isValid: function (b) { return typeof b.label === "string"; },
            messages: {
                load: "Отметките не можаха да се заредят.",
                add: "Отметката не можа да се запази.",
                remove: "Отметката не можа да се изтрие.",
                limit: "Можете да имате най-много " + BOOKMARKS_MAX + " отметки в една книга."
            }
        });
    }

    function byPercent(a, b) {
        return (a.percent || 0) - (b.percent || 0);
    }

    function wireBookmarks(book, rendition, sync) {
        var list = el("sl-epub-bookmark-list");
        var addButton = el("sl-epub-bookmark-add");
        var empty = el("sl-epub-bookmark-empty");
        var error = el("sl-epub-bookmark-error");
        var icon = el("sl-epub-bookmark-icon");
        if (!list || !addButton) {
            return;
        }

        var store = createBookmarkStore();
        var bookmarks = [];
        var cfiTool = typeof ePub.CFI === "function" ? new ePub.CFI() : null;

        function showError(message) {
            if (!error) {
                return;
            }
            error.textContent = message || "";
            error.classList.toggle("d-none", !message);
        }

        // Whether a bookmark falls on the page that is on screen (fills the icon)
        function onCurrentPage(bookmark) {
            var current = rendition.currentLocation();
            if (!current || !current.start) {
                return false;
            }
            if (bookmark.cfi === current.start.cfi) {
                return true;
            }
            try {
                return !!cfiTool && !!current.end &&
                    cfiTool.compare(bookmark.cfi, current.start.cfi) >= 0 &&
                    cfiTool.compare(bookmark.cfi, current.end.cfi) <= 0;
            } catch (e) {
                return false;
            }
        }

        function updateIcon() {
            if (icon) {
                icon.className = bookmarks.some(onCurrentPage) ? "bi bi-bookmark-fill" : "bi bi-bookmark";
            }
        }

        function render() {
            list.textContent = "";
            bookmarks.forEach(function (bookmark) {
                var li = document.createElement("li");
                li.className = "d-flex align-items-start gap-1";

                var go = document.createElement("button");
                go.type = "button";
                go.className = "sl-epub-item flex-grow-1";
                var percent = document.createElement("span");
                percent.className = "badge text-bg-secondary me-1";
                percent.textContent = (bookmark.percent || 0) + "%";
                go.appendChild(percent);
                go.appendChild(document.createTextNode(bookmark.label));
                go.addEventListener("click", function () {
                    rendition.display(bookmark.cfi);
                    closeDropdown("sl-epub-bookmarks-btn");
                });

                var remove = document.createElement("button");
                remove.type = "button";
                remove.className = "btn btn-sm btn-link text-danger p-1";
                remove.setAttribute("aria-label", "Изтрий отметката");
                remove.title = "Изтрий";
                var trash = document.createElement("i");
                trash.className = "bi bi-trash";
                remove.appendChild(trash);
                remove.addEventListener("click", function () {
                    showError("");
                    store.remove(bookmark)
                        .then(function () {
                            bookmarks = bookmarks.filter(function (b) { return b !== bookmark; });
                            render();
                        })
                        .catch(function (e) { showError(e.message); });
                });

                li.appendChild(go);
                li.appendChild(remove);
                list.appendChild(li);
            });
            if (empty) {
                empty.classList.toggle("d-none", bookmarks.length > 0);
            }
            updateIcon();
        }

        addButton.addEventListener("click", function () {
            var current = rendition.currentLocation();
            if (!current || !current.start) {
                return;
            }
            var chapter = chapterTitle(book, current.start.href);
            var text = pageText(rendition, current);
            var quote = text ? "„" + text + "…“" : "";
            var label = chapter && quote ? chapter + " — " + quote : (chapter || quote || "Отметка");

            showError("");
            addButton.disabled = true;
            store.add({ cfi: current.start.cfi, label: label, percent: sync.percent() })
                .then(function (saved) {
                    if (!bookmarks.some(function (b) { return b.id === saved.id; })) {
                        bookmarks.push(saved);
                        bookmarks.sort(byPercent);
                    }
                    render();
                })
                .catch(function (e) { showError(e.message); })
                .then(function () { addButton.disabled = false; });
        });

        rendition.on("relocated", updateIcon);

        store.list()
            .then(function (loaded) {
                bookmarks = (loaded || []).slice().sort(byPercent);
                render();
            })
            .catch(function (e) { showError(e.message); });
    }

    // --- search in the book: chapter by chapter, in the browser (the text never goes to the server) ---
    var SEARCH_MIN = 2;
    var SEARCH_MAX_RESULTS = 200;

    // The excerpt as text with the found words in <mark> (built from text nodes, never as HTML)
    function excerptNode(excerpt, query) {
        var span = document.createElement("span");
        var lower = excerpt.toLowerCase();
        var needle = query.toLowerCase();
        var at = 0;
        var index = lower.indexOf(needle);
        while (index >= 0) {
            span.appendChild(document.createTextNode(excerpt.slice(at, index)));
            var mark = document.createElement("mark");
            mark.textContent = excerpt.slice(index, index + needle.length);
            span.appendChild(mark);
            at = index + needle.length;
            index = lower.indexOf(needle, at);
        }
        span.appendChild(document.createTextNode(excerpt.slice(at)));
        return span;
    }

    function wireSearch(book, rendition) {
        var form = el("sl-epub-search-form");
        var input = el("sl-epub-search-input");
        var status = el("sl-epub-search-status");
        var results = el("sl-epub-search-results");
        if (!form || !input || !results) {
            return;
        }

        var searchId = 0;
        var highlighted = null;

        function setStatus(text) {
            if (status) {
                status.textContent = text;
            }
        }

        // Marks the found words on the page; the previous mark is removed
        function highlight(cfi) {
            try {
                if (highlighted) {
                    rendition.annotations.remove(highlighted, "highlight");
                }
                rendition.annotations.highlight(cfi, {}, null, "sl-epub-hit");
                highlighted = cfi;
            } catch (e) {
                highlighted = null;
            }
        }

        function addResult(result, query, chapter) {
            var li = document.createElement("li");
            var go = document.createElement("button");
            go.type = "button";
            go.className = "sl-epub-item";
            if (chapter) {
                var title = document.createElement("div");
                title.className = "fw-semibold small text-body-secondary";
                title.textContent = chapter;
                go.appendChild(title);
            }
            go.appendChild(excerptNode(result.excerpt || "", query));
            go.addEventListener("click", function () {
                rendition.display(result.cfi).then(function () { highlight(result.cfi); });
                closeDropdown("sl-epub-search-btn");
            });
            li.appendChild(go);
            results.appendChild(li);
        }

        form.addEventListener("submit", function (e) {
            e.preventDefault();
            var query = input.value.replace(/\s+/g, " ").trim();
            results.textContent = "";
            if (query.length < SEARCH_MIN) {
                setStatus("Въведете поне " + SEARCH_MIN + " знака.");
                return;
            }

            var id = ++searchId;
            var found = 0;
            var items = (book.spine && book.spine.spineItems) || [];
            setStatus("Търсене…");

            // One chapter at a time, so a large book is not loaded all at once; a new search stops this one
            items.reduce(function (chain, item) {
                return chain.then(function () {
                    if (id !== searchId || found >= SEARCH_MAX_RESULTS) {
                        return null;
                    }
                    return item.load(book.load.bind(book))
                        .then(function () {
                            if (id !== searchId) {
                                return;
                            }
                            var chapter = chapterTitle(book, item.href);
                            // search() also finds words split between elements (e.g. a word half in italics)
                            var hits = typeof item.search === "function" ? item.search(query) : item.find(query);
                            (hits || []).forEach(function (result) {
                                if (found < SEARCH_MAX_RESULTS) {
                                    found++;
                                    addResult(result, query, chapter);
                                }
                            });
                        })
                        .catch(function () { /* a chapter that cannot be read is skipped */ })
                        .then(function () { item.unload(); });
                });
            }, Promise.resolve())
                .then(function () {
                    if (id !== searchId) {
                        return;
                    }
                    if (found === 0) {
                        setStatus("Няма намерени резултати.");
                    } else if (found >= SEARCH_MAX_RESULTS) {
                        setStatus("Показани са първите " + SEARCH_MAX_RESULTS + " резултата. Уточнете търсенето.");
                    } else {
                        setStatus("Намерени: " + found);
                    }
                });
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
