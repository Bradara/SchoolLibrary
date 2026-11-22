// Highlights and notes in the EPUB reader. Waits for sl-epub-reader.js to open the book ("sl-epub-ready").
// Select text in the book → choose a color or write a note. Click a highlight → change it or remove it.
// A signed-in reader's highlights are kept on the server (every device); a visitor's stay in this browser.
// Everything from the book or from the user is put on the page as text, never as HTML.
(function () {
    "use strict";

    var area = document.getElementById("sl-epub");
    if (!area) {
        return;
    }

    var COLORS = { yellow: "#ffd400", green: "#4cc44c", blue: "#4da3ff", pink: "#ff6fb5" };
    var DEFAULT_COLOR = "yellow";
    var HIGHLIGHTS_MAX = 500;
    var NOTE_MAX = 2000;
    var TEXT_MAX = 1000;

    function colorOf(item) {
        return COLORS.hasOwnProperty(item.color) ? item.color : DEFAULT_COLOR;
    }

    function oneLine(text, max) {
        var value = (text || "").replace(/\s+/g, " ").trim();
        return value.length > max ? value.slice(0, max - 1) + "…" : value;
    }

    area.addEventListener("sl-epub-ready", function (event) {
        start(event.detail);
    });

    function start(reader) {
        var book = reader.book;
        var rendition = reader.rendition;
        var h = reader.helpers;
        var el = h.el;

        var bar = el("sl-epub-selection");
        var barText = el("sl-epub-selection-text");
        var colorButtons = bar ? bar.querySelectorAll(".sl-epub-color") : [];
        var noteToggle = el("sl-epub-note-toggle");
        var noteEditor = el("sl-epub-note-editor");
        var noteInput = el("sl-epub-note");
        var noteCount = el("sl-epub-note-count");
        var noteSave = el("sl-epub-note-save");
        var removeButton = el("sl-epub-highlight-delete");
        var closeButton = el("sl-epub-selection-close");
        var barError = el("sl-epub-selection-error");

        var list = el("sl-epub-notes-list");
        var listEmpty = el("sl-epub-notes-empty");
        var listError = el("sl-epub-notes-error");
        var listCount = el("sl-epub-notes-count");
        var exportButton = el("sl-epub-notes-export");

        if (!bar || !list) {
            return;
        }

        var store = h.createStore({
            urls: {
                list: area.getAttribute("data-epub-highlights-url"),
                add: area.getAttribute("data-epub-highlight-add-url"),
                update: area.getAttribute("data-epub-highlight-update-url"),
                remove: area.getAttribute("data-epub-highlight-delete-url")
            },
            localKey: h.positionKey + "-highlights",
            idField: "highlightId",
            resultField: "highlight",
            max: HIGHLIGHTS_MAX,
            isValid: function (item) { return typeof item.text === "string"; },
            messages: {
                load: "Бележките не можаха да се заредят.",
                add: "Подчертаването не можа да се запази.",
                remove: "Подчертаването не можа да се премахне.",
                limit: "Можете да имате най-много " + HIGHLIGHTS_MAX + " подчертавания в една книга."
            }
        });

        var highlights = [];
        // What the bar works on: a new selection { cfi, text, contents } or an existing highlight
        var selection = null;
        var editing = null;
        var busy = false;

        function showMessage(node, message) {
            if (node) {
                node.textContent = message || "";
                node.classList.toggle("d-none", !message);
            }
        }

        // --- the marks in the book ---
        function draw(item) {
            try {
                rendition.annotations.highlight(item.cfi, { id: item.id }, function () { openFor(item); },
                    "sl-epub-mark", { fill: COLORS[colorOf(item)], "fill-opacity": "0.35" });
                if (item.note) {
                    rendition.annotations.underline(item.cfi, { id: item.id }, function () { openFor(item); },
                        "sl-epub-note-mark", { stroke: COLORS[colorOf(item)], "stroke-opacity": "0.9" });
                }
            } catch (e) {
                // a highlight from another version of the book cannot be placed; it stays in the list
            }
        }

        function erase(item) {
            try {
                rendition.annotations.remove(item.cfi, "highlight");
                rendition.annotations.remove(item.cfi, "underline");
            } catch (e) {
                // nothing drawn
            }
        }

        // --- the bar ---
        function clearSelection() {
            if (selection && selection.contents) {
                try {
                    selection.contents.window.getSelection().removeAllRanges();
                } catch (e) {
                    // the chapter may already be gone
                }
            }
        }

        function setActiveColor(color) {
            Array.prototype.forEach.call(colorButtons, function (button) {
                var active = button.getAttribute("data-color") === color;
                button.classList.toggle("active", active);
                button.setAttribute("aria-pressed", active ? "true" : "false");
            });
        }

        function showEditor(show) {
            noteEditor.classList.toggle("d-none", !show);
            noteToggle.setAttribute("aria-expanded", show ? "true" : "false");
            if (show) {
                updateCount();
                noteInput.focus();
            }
        }

        function updateCount() {
            if (noteCount) {
                noteCount.textContent = noteInput.value.length + " / " + NOTE_MAX;
            }
        }

        function closeBar() {
            clearSelection();
            selection = null;
            editing = null;
            bar.classList.add("d-none");
            showMessage(barError, "");
        }

        function openForSelection(cfi, contents) {
            var text = "";
            try {
                text = rendition.getRange(cfi).toString();
            } catch (e) {
                text = "";
            }
            text = oneLine(text, TEXT_MAX);
            if (!text) {
                return;
            }

            // The same passage again: edit the existing highlight instead
            var existing = highlights.filter(function (item) { return item.cfi === cfi; })[0];
            if (existing) {
                openFor(existing);
                return;
            }

            selection = { cfi: cfi, text: text, contents: contents };
            editing = null;
            barText.textContent = "„" + oneLine(text, 120) + "“";
            setActiveColor(null);
            noteInput.value = "";
            showEditor(false);
            removeButton.classList.add("d-none");
            showMessage(barError, "");
            bar.classList.remove("d-none");
        }

        function openFor(item) {
            clearSelection();
            selection = null;
            editing = item;
            barText.textContent = "„" + oneLine(item.text, 120) + "“";
            setActiveColor(colorOf(item));
            noteInput.value = item.note || "";
            showEditor(!!item.note);
            removeButton.classList.remove("d-none");
            showMessage(barError, "");
            bar.classList.remove("d-none");
        }

        function percentOf(cfi) {
            try {
                if (book.locations && book.locations.length()) {
                    return Math.round((book.locations.percentageFromCfi(cfi) || 0) * 100);
                }
            } catch (e) {
                // fall back to the page on screen
            }
            return reader.percent();
        }

        function run(action) {
            if (busy) {
                return;
            }
            busy = true;
            showMessage(barError, "");
            action()
                .catch(function (e) { showMessage(barError, e.message); })
                .then(function () { busy = false; });
        }

        function create(color, note) {
            var chosen = selection;
            run(function () {
                return store.add({
                    cfi: chosen.cfi,
                    text: chosen.text,
                    color: color,
                    note: note || "",
                    percent: percentOf(chosen.cfi)
                }).then(function (saved) {
                    if (!highlights.some(function (item) { return item.id === saved.id; })) {
                        highlights.push(saved);
                        draw(saved);
                        render();
                    }
                    // Quick highlighting: the bar goes away; a click on the mark brings it back for a note
                    closeBar();
                });
            });
        }

        function change(item, color, note) {
            run(function () {
                return store.update(item, { color: color, note: note || "" }).then(function (saved) {
                    erase(item);
                    highlights = highlights.map(function (x) { return x.id === item.id ? saved : x; });
                    draw(saved);
                    render();
                    openFor(saved);
                });
            });
        }

        Array.prototype.forEach.call(colorButtons, function (button) {
            button.addEventListener("click", function () {
                var color = button.getAttribute("data-color");
                if (editing) {
                    change(editing, color, editing.note);
                } else if (selection) {
                    create(color, noteInput.value.trim());
                }
            });
        });

        noteToggle.addEventListener("click", function () {
            showEditor(noteEditor.classList.contains("d-none"));
        });

        noteInput.addEventListener("input", updateCount);

        noteSave.addEventListener("click", function () {
            var note = noteInput.value.trim();
            if (note.length > NOTE_MAX) {
                showMessage(barError, "Бележката може да е до " + NOTE_MAX + " знака.");
                return;
            }
            if (editing) {
                change(editing, colorOf(editing), note);
            } else if (selection) {
                if (!note) {
                    showMessage(barError, "Напишете бележка или изберете цвят.");
                    return;
                }
                create(DEFAULT_COLOR, note);
            }
        });

        removeButton.addEventListener("click", function () {
            var item = editing;
            if (!item) {
                return;
            }
            run(function () {
                return store.remove(item).then(function () {
                    erase(item);
                    highlights = highlights.filter(function (x) { return x.id !== item.id; });
                    render();
                    closeBar();
                });
            });
        });

        closeButton.addEventListener("click", closeBar);

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && !bar.classList.contains("d-none")) {
                closeBar();
            }
        });

        rendition.on("selected", function (cfiRange, contents) {
            openForSelection(cfiRange, contents);
        });

        // --- the list of notes ---
        function render() {
            list.textContent = "";
            highlights.sort(function (a, b) { return (a.percent || 0) - (b.percent || 0); });
            highlights.forEach(function (item) {
                var li = document.createElement("li");
                li.className = "d-flex align-items-start gap-1";

                var go = document.createElement("button");
                go.type = "button";
                go.className = "sl-epub-item flex-grow-1";

                var head = document.createElement("div");
                head.className = "d-flex align-items-center gap-1 mb-1";
                var swatch = document.createElement("span");
                swatch.className = "sl-epub-swatch sl-epub-swatch-" + colorOf(item);
                var percent = document.createElement("span");
                percent.className = "badge text-bg-secondary";
                percent.textContent = (item.percent || 0) + "%";
                head.appendChild(swatch);
                head.appendChild(percent);
                go.appendChild(head);

                var quote = document.createElement("div");
                quote.className = "sl-epub-quote";
                quote.textContent = "„" + item.text + "“";
                go.appendChild(quote);

                if (item.note) {
                    var note = document.createElement("div");
                    note.className = "sl-epub-note-text small mt-1";
                    note.textContent = item.note;
                    go.appendChild(note);
                }

                go.addEventListener("click", function () {
                    rendition.display(item.cfi);
                    h.closeDropdown("sl-epub-notes-btn");
                });

                var edit = document.createElement("button");
                edit.type = "button";
                edit.className = "btn btn-sm btn-link p-1";
                edit.title = "Промени";
                edit.setAttribute("aria-label", "Промени подчертаването и бележката");
                var pencil = document.createElement("i");
                pencil.className = "bi bi-pencil";
                edit.appendChild(pencil);
                edit.addEventListener("click", function () {
                    rendition.display(item.cfi);
                    h.closeDropdown("sl-epub-notes-btn");
                    openFor(item);
                });

                li.appendChild(go);
                li.appendChild(edit);
                list.appendChild(li);
            });

            var count = highlights.length;
            if (listEmpty) { listEmpty.classList.toggle("d-none", count > 0); }
            if (exportButton) { exportButton.classList.toggle("d-none", count === 0); }
            if (listCount) {
                listCount.textContent = String(count);
                listCount.classList.toggle("d-none", count === 0);
            }
        }

        // --- export: a Markdown file made in the browser ---
        function bookTitle() {
            try {
                return (book.packaging && book.packaging.metadata && book.packaging.metadata.title) || "";
            } catch (e) {
                return "";
            }
        }

        function safeFileName(text) {
            return (text || "книга").replace(/[\\/:*?"<>|\u0000-\u001f]+/g, " ").replace(/\s+/g, " ").trim().slice(0, 80) || "книга";
        }

        if (exportButton) {
            exportButton.addEventListener("click", function () {
                var title = bookTitle();
                var lines = ["# Бележки" + (title ? ": " + title : ""), ""];
                highlights.forEach(function (item) {
                    lines.push("> " + item.text.replace(/\n/g, " "));
                    lines.push("");
                    if (item.note) {
                        lines.push(item.note);
                        lines.push("");
                    }
                    lines.push("*" + (item.percent || 0) + "% от книгата*");
                    lines.push("");
                    lines.push("---");
                    lines.push("");
                });
                var blob = new Blob([lines.join("\n")], { type: "text/markdown;charset=utf-8" });
                var link = document.createElement("a");
                link.href = URL.createObjectURL(blob);
                link.download = "Бележки - " + safeFileName(title) + ".md";
                document.body.appendChild(link);
                link.click();
                document.body.removeChild(link);
                window.setTimeout(function () { URL.revokeObjectURL(link.href); }, 1000);
            });
        }

        store.list()
            .then(function (loaded) {
                highlights = (loaded || []).slice();
                highlights.forEach(draw);
                render();
            })
            .catch(function (e) { showMessage(listError, e.message); });
    }
})();
