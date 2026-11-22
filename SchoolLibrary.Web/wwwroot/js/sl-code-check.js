// "Провери" on code questions (_CodeAnswerInput): sends the program to the server, shows how the tests went
// and keeps the count of wrong checks. Everything the program printed is put on the page as text, never as HTML.
// In an exam the server counts the checks; while practising the count is kept in a hidden field of the form.
(function () {
    "use strict";

    function element(tag, className, text) {
        var node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = text;
        }
        return node;
    }

    function pre(text, className) {
        var node = element("pre", "mb-0 " + (className || ""), text || "");
        return node;
    }

    function token(form) {
        var input = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : "";
    }

    function renderTests(tests) {
        var wrapper = element("div", "table-responsive");
        var table = element("table", "table table-sm table-bordered align-top mb-0 small");
        var head = element("thead");
        var headRow = element("tr");
        ["Тест", "Вход", "Очакван изход", "Изход на програмата"].forEach(function (title) {
            var th = element("th", null, title);
            th.scope = "col";
            headRow.appendChild(th);
        });
        head.appendChild(headRow);
        table.appendChild(head);

        var body = element("tbody");
        tests.forEach(function (test, index) {
            var row = element("tr", test.passed ? "table-success" : "table-danger");
            row.appendChild(element("td", "text-nowrap", (test.passed ? "✓ " : "✗ ") + (index + 1)));
            if (test.hidden) {
                var hidden = element("td", "fst-italic text-body-secondary", "Скрит тест");
                hidden.colSpan = 3;
                row.appendChild(hidden);
            } else {
                var input = element("td");
                input.appendChild(pre(test.stdin));
                var expected = element("td");
                expected.appendChild(pre(test.expected));
                var output = element("td");
                output.appendChild(pre(test.output));
                if (test.timedOut) {
                    output.appendChild(element("div", "text-danger", "Превишено време."));
                }
                if (test.error) {
                    output.appendChild(pre(test.error, "text-danger"));
                }
                row.appendChild(input);
                row.appendChild(expected);
                row.appendChild(output);
            }
            body.appendChild(row);
        });
        table.appendChild(body);
        wrapper.appendChild(table);
        return wrapper;
    }

    function render(box, data) {
        box.replaceChildren();
        if (!data.ok) {
            box.appendChild(element("div", "alert alert-warning py-2 small mb-0", data.message || "Проверката не е възможна."));
            return;
        }

        var tests = data.tests || [];
        var passedCount = tests.filter(function (t) { return t.passed; }).length;
        if (data.status === "CompileError") {
            var compile = element("div", "alert alert-danger py-2 small mb-2");
            compile.appendChild(element("div", "fw-semibold", "Грешка при компилиране"));
            compile.appendChild(pre(data.message, "small text-wrap"));
            box.appendChild(compile);
        } else if (data.status !== "Graded") {
            box.appendChild(element("div", "alert alert-warning py-2 small mb-2", data.message || "Програмата не беше проверена."));
        } else if (data.passed) {
            box.appendChild(element("div", "alert alert-success py-2 small mb-2", "Всички тестове минаха (" + tests.length + " от " + tests.length + ")."));
        } else {
            box.appendChild(element("div", "alert alert-danger py-2 small mb-2", "Минати тестове: " + passedCount + " от " + tests.length + "."));
        }

        if (data.status === "Graded" && tests.length > 0) {
            box.appendChild(renderTests(tests));
        }

        if (!data.counted) {
            box.appendChild(element("p", "form-text mb-0", "Тази проверка не се брои."));
        }
    }

    document.addEventListener("click", function (event) {
        var button = event.target.closest("[data-code-check-button]");
        if (!button) {
            return;
        }

        var box = button.closest("[data-code-check]");
        var form = button.closest("form");
        var code = document.getElementById(box.dataset.codeId);
        var result = box.querySelector("[data-code-check-result]");
        var counter = box.querySelector("[data-code-check-count]");
        var hidden = box.querySelector("[data-code-check-wrong]");
        var label = button.textContent;

        var body = new FormData();
        body.append("questionId", box.dataset.questionId);
        body.append("code", code ? code.value : "");
        body.append("__RequestVerificationToken", token(form));

        button.disabled = true;
        button.textContent = "Проверява се…";

        fetch(box.dataset.url, { method: "POST", body: body, credentials: "same-origin" })
            .then(function (response) {
                return response.ok ? response.json() : { ok: false, message: "Проверката не е възможна в момента." };
            })
            .catch(function () {
                return { ok: false, message: "Няма връзка със сървъра. Опитайте отново." };
            })
            .then(function (data) {
                render(result, data);

                if (data.ok && data.counted) {
                    var wrong = Number(counter.dataset.wrong || "0");
                    // The exam's count comes from the server; while practising it is kept here
                    wrong = data.wrongChecks !== null && data.wrongChecks !== undefined
                        ? data.wrongChecks
                        : wrong + (data.passed ? 0 : 1);
                    counter.dataset.wrong = String(wrong);
                    counter.textContent = "Грешни проверки: " + wrong;
                    if (hidden) {
                        hidden.value = String(wrong);
                    }
                }

                button.textContent = label;
                button.disabled = data.ok && data.checksLeft === 0;
            });
    });
})();
