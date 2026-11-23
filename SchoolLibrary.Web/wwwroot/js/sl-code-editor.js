// Code editor with line numbers, syntax colouring and indentation (CodeMirror 5, self-hosted in lib/codemirror).
// Loaded on every page, but CodeMirror itself is fetched only when the page has code on it:
//   <textarea data-code-editor data-code-language="csharp">  – an editable box; the textarea keeps the value,
//                                                               so forms and "Провери" read it as before
//   data-code-language-from="#select-id"                     – the language follows a <select> (question editor)
//   <pre data-code-view data-code-language="python3">         – read-only view of a submitted program
// Editable boxes get completion hints (sl-code-hints.js), except inside an element with data-code-hints="off"
// (an exam where the teacher turned them off).
// Without JavaScript the plain textarea / pre still work.
(function () {
    "use strict";

    var selector = "textarea[data-code-editor], pre[data-code-view]";
    if (!document.querySelector(selector)) {
        return;
    }

    var script = document.currentScript;
    var base = (script && script.dataset.lib) || "/lib/codemirror/";
    var hintsScript = (script && script.dataset.hints) || "/js/sl-code-hints.js";
    var maxLength = 10000;

    var modes = {
        csharp: { mode: "text/x-csharp", indentUnit: 4 },
        python3: { mode: "text/x-python", indentUnit: 4 }
    };

    function loadCss(href) {
        var link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = base + href;
        document.head.appendChild(link);
    }

    function loadScript(src, absolute) {
        return new Promise(function (resolve, reject) {
            var node = document.createElement("script");
            node.src = absolute ? src : base + src;
            node.onload = resolve;
            node.onerror = reject;
            document.head.appendChild(node);
        });
    }

    function isDark() {
        return document.documentElement.getAttribute("data-bs-theme") === "dark";
    }

    function themeName() {
        return isDark() ? "dracula" : "default";
    }

    function modeFor(language) {
        return modes[language] || { mode: "text/plain", indentUnit: 4 };
    }

    function applyLanguage(editor, language) {
        var settings = modeFor(language);
        editor.setOption("mode", settings.mode);
        editor.setOption("indentUnit", settings.indentUnit);
    }

    // Tab indents (or indents the selected lines), Shift+Tab outdents; Esc then Tab leaves the editor,
    // so keyboard users are not trapped inside it
    var escaped = false;
    var keys = {
        Tab: function (cm) {
            if (escaped) {
                escaped = false;
                return window.CodeMirror.Pass;
            }
            if (cm.somethingSelected()) {
                cm.indentSelection("add");
            } else {
                cm.replaceSelection(Array(cm.getOption("indentUnit") + 1).join(" "), "end");
            }
        },
        "Shift-Tab": function (cm) {
            if (escaped) {
                escaped = false;
                return window.CodeMirror.Pass;
            }
            cm.indentSelection("subtract");
        },
        Esc: function () {
            escaped = true;
        }
    };

    // CodeMirror measures itself when created; one created inside a hidden block (a closed <details>,
    // an inactive question type) is redrawn when the block becomes visible
    function refreshWhenShown(editor) {
        if (!window.ResizeObserver) {
            return;
        }
        var wrapper = editor.getWrapperElement();
        var visible = wrapper.offsetParent !== null;
        new ResizeObserver(function () {
            var now = wrapper.offsetParent !== null;
            if (now && !visible) {
                editor.refresh();
            }
            visible = now;
        }).observe(wrapper);
    }

    function createEditor(textarea) {
        var languageSource = textarea.dataset.codeLanguageFrom
            ? document.querySelector(textarea.dataset.codeLanguageFrom)
            : null;
        var language = languageSource ? languageSource.value : textarea.dataset.codeLanguage;
        var settings = modeFor(language);
        var limit = Number(textarea.getAttribute("maxlength")) || maxLength;

        var editor = window.CodeMirror.fromTextArea(textarea, {
            mode: settings.mode,
            indentUnit: settings.indentUnit,
            tabSize: 4,
            indentWithTabs: false,
            lineNumbers: true,
            matchBrackets: true,
            autoCloseBrackets: true,
            smartIndent: true,
            electricChars: true,
            theme: themeName(),
            extraKeys: keys,
            spellcheck: false,
            autocorrect: false,
            autocapitalize: false,
            screenReaderLabel: textarea.labels && textarea.labels[0] ? textarea.labels[0].textContent : "Код"
        });
        editor.getWrapperElement().classList.add("sl-code-editor", "form-control", "p-0");
        var rows = Number(textarea.getAttribute("rows")) || 12;
        editor.setSize(null, (rows * 1.5 + 0.5) + "em");
        // Measure the gutter again now that the site's styles apply to it
        editor.refresh();

        // Keep the textarea up to date: "Провери" and the exam timer's auto-submit read its value
        editor.on("change", function () {
            editor.save();
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
        });
        // The textarea's maxlength does not apply to CodeMirror
        editor.on("beforeChange", function (cm, change) {
            if (change.origin === "setValue" || !change.update) {
                return;
            }
            var removed = cm.indexFromPos(change.to) - cm.indexFromPos(change.from);
            var added = change.text.join("\n").length;
            if (cm.getValue().length - removed + added > limit) {
                change.cancel();
            }
        });

        editor.on("keydown", function (cm, event) {
            if (event.key !== "Escape" && event.key !== "Tab") {
                escaped = false;
            }
        });

        if (languageSource) {
            languageSource.addEventListener("change", function () {
                applyLanguage(editor, languageSource.value);
            });
        }

        if (window.SlCodeHints && !textarea.closest('[data-code-hints="off"]')) {
            window.SlCodeHints.attach(editor, function () {
                return languageSource ? languageSource.value : language;
            });
        }

        refreshWhenShown(editor);
        return editor;
    }

    function createView(pre) {
        var text = pre.textContent.replace(/\n$/, "");
        var holder = document.createElement("div");
        holder.className = "sl-code-editor sl-code-view border rounded small";
        pre.replaceWith(holder);
        var editor = window.CodeMirror(holder, {
            value: text,
            mode: modeFor(pre.dataset.codeLanguage).mode,
            tabSize: 4,
            lineNumbers: true,
            readOnly: true,
            theme: themeName(),
            viewportMargin: Infinity
        });
        editor.setSize(null, "auto");
        refreshWhenShown(editor);
        return editor;
    }

    loadCss("codemirror.min.css");
    loadCss("theme/dracula.min.css");
    loadCss("addon/show-hint.min.css");

    loadScript("codemirror.min.js")
        .then(function () {
            return Promise.all([
                loadScript("mode/clike.min.js"),
                loadScript("mode/python.min.js"),
                loadScript("addon/matchbrackets.min.js"),
                loadScript("addon/closebrackets.min.js"),
                loadScript("addon/show-hint.min.js"),
                loadScript(hintsScript, true)
            ]);
        })
        .then(function () {
            var editors = [];
            document.querySelectorAll(selector).forEach(function (node) {
                editors.push(node.tagName === "PRE" ? createView(node) : createEditor(node));
            });

            // Follow the site's theme switcher
            new MutationObserver(function () {
                editors.forEach(function (editor) {
                    editor.setOption("theme", themeName());
                });
            }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });
        })
        .catch(function () {
            // CodeMirror did not load: the plain textarea / pre stay in place
        });
})();
