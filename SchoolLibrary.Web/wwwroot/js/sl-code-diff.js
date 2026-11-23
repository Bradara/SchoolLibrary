// "Покажи разликите" for code test results: highlights where the program's output differs from the expected one.
// Markup: a button [data-diff-toggle] inside an element [data-diff-scope]; each failed test in it has
// <pre data-diff="expected"> and <pre data-diff="actual">. The texts are compared like on the server
// (CodeOutput.Normalize): line endings unified, spaces at the end of a line and empty lines at the end ignored.
// Everything is put on the page as text, never as HTML.
(function () {
    "use strict";

    var maxLines = 500;
    var maxLineLength = 1000;
    // Below this share of common characters two lines count as different as a whole: marking the few letters
    // they happen to share would only be noise
    var minSimilarity = 0.6;

    function normalize(text) {
        var lines = (text || "").replace(/\r\n?/g, "\n").split("\n").map(function (line) {
            return line.replace(/\s+$/, "");
        });
        while (lines.length > 0 && lines[lines.length - 1] === "") {
            lines.pop();
        }
        return lines;
    }

    // Longest common subsequence of two arrays → for each item of a and b whether it is common
    function common(a, b) {
        var n = a.length;
        var m = b.length;
        var table = [];
        for (var i = 0; i <= n; i++) {
            table.push(new Uint16Array(m + 1));
        }
        for (i = n - 1; i >= 0; i--) {
            for (var j = m - 1; j >= 0; j--) {
                table[i][j] = a[i] === b[j]
                    ? table[i + 1][j + 1] + 1
                    : Math.max(table[i + 1][j], table[i][j + 1]);
            }
        }

        var inA = new Array(n).fill(false);
        var inB = new Array(m).fill(false);
        var pairs = [];
        i = 0;
        j = 0;
        while (i < n && j < m) {
            if (a[i] === b[j]) {
                inA[i] = true;
                inB[j] = true;
                pairs.push([i, j]);
                i++;
                j++;
            } else if (table[i + 1][j] >= table[i][j + 1]) {
                i++;
            } else {
                j++;
            }
        }
        return { inA: inA, inB: inB, pairs: pairs };
    }

    // A highlighted piece; its spaces and tabs are made visible
    function mark(text) {
        var node = document.createElement("mark");
        node.className = "sl-diff";
        node.textContent = text.replace(/ /g, "·").replace(/\t/g, "→");
        return node;
    }

    // Appends a line to a <pre>, highlighting the characters that are not common with the other line
    function appendLine(target, line, flags) {
        var start = 0;
        while (start < line.length) {
            var different = !flags[start];
            var end = start;
            while (end < line.length && !flags[end] === different) {
                end++;
            }
            var piece = line.slice(start, end);
            target.appendChild(different ? mark(piece) : document.createTextNode(piece));
            start = end;
        }
    }

    function appendMissingLine(target) {
        var node = mark("⏎");
        node.title = "Празен ред";
        target.appendChild(node);
    }

    // Builds the highlighted contents of both sides
    function diff(expectedText, actualText) {
        var expected = normalize(expectedText).slice(0, maxLines);
        var actual = normalize(actualText).slice(0, maxLines);
        var lines = common(expected, actual);

        // Lines that are not common are paired in order between two common lines, then compared by characters
        var expectedFlags = expected.map(function (line, i) {
            return lines.inA[i] ? null : new Array(line.length).fill(false);
        });
        var actualFlags = actual.map(function (line, i) {
            return lines.inB[i] ? null : new Array(line.length).fill(false);
        });
        var anchors = lines.pairs.concat([[expected.length, actual.length]]);
        var prevA = 0;
        var prevB = 0;
        anchors.forEach(function (anchor) {
            for (var k = 0; prevA + k < anchor[0] && prevB + k < anchor[1]; k++) {
                var a = expected[prevA + k];
                var b = actual[prevB + k];
                if (a.length <= maxLineLength && b.length <= maxLineLength) {
                    var chars = common(a.split(""), b.split(""));
                    if (2 * chars.pairs.length >= minSimilarity * (a.length + b.length)) {
                        expectedFlags[prevA + k] = chars.inA;
                        actualFlags[prevB + k] = chars.inB;
                    }
                }
            }
            prevA = anchor[0] + 1;
            prevB = anchor[1] + 1;
        });

        function build(list, flags) {
            var fragment = document.createDocumentFragment();
            list.forEach(function (line, i) {
                if (i > 0) {
                    fragment.appendChild(document.createTextNode("\n"));
                }
                if (flags[i] === null) {
                    fragment.appendChild(document.createTextNode(line));
                } else if (line.length === 0) {
                    appendMissingLine(fragment);
                } else {
                    appendLine(fragment, line, flags[i]);
                }
            });
            return fragment;
        }

        return { expected: build(expected, expectedFlags), actual: build(actual, actualFlags) };
    }

    function show(pre, content) {
        if (pre.dataset.diffOriginal === undefined) {
            pre.dataset.diffOriginal = pre.textContent;
        }
        pre.replaceChildren(content);
    }

    function restore(pre) {
        if (pre.dataset.diffOriginal !== undefined) {
            pre.textContent = pre.dataset.diffOriginal;
            delete pre.dataset.diffOriginal;
        }
    }

    function toggle(button) {
        var scope = button.closest("[data-diff-scope]");
        if (!scope) {
            return;
        }
        var on = button.getAttribute("aria-pressed") !== "true";
        scope.querySelectorAll("[data-diff-row]").forEach(function (row) {
            var expected = row.querySelector('[data-diff="expected"]');
            var actual = row.querySelector('[data-diff="actual"]');
            if (!expected || !actual) {
                return;
            }
            if (on) {
                var result = diff(expected.dataset.diffOriginal ?? expected.textContent,
                    actual.dataset.diffOriginal ?? actual.textContent);
                show(expected, result.expected);
                show(actual, result.actual);
            } else {
                restore(expected);
                restore(actual);
            }
        });
        button.setAttribute("aria-pressed", on ? "true" : "false");
        button.classList.toggle("active", on);
        var label = button.querySelector("[data-diff-label]") || button;
        label.textContent = on ? "Скрий разликите" : "Покажи разликите";
        var legend = scope.querySelector("[data-diff-legend]");
        if (legend) {
            legend.hidden = !on;
        }
    }

    document.addEventListener("click", function (event) {
        var button = event.target.closest("[data-diff-toggle]");
        if (button) {
            toggle(button);
        }
    });

    window.SlCodeDiff = { diff: diff };
})();
