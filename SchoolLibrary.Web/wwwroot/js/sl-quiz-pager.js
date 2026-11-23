// One question per screen (partial _QuizQuestionList): shows one [data-quiz-page] at a time, with
// "Предишен/Следващ въпрос", a numbered navigator that marks the answered questions, and the submit button
// on the last screen and under the navigator. All answers stay in the one form; nothing is sent until submit.
// Submitting with unanswered questions first lists them and asks again (on the page, not with a browser dialog,
// which would count as leaving an exam). The timer's and proctoring's own submits are not stopped.
(function () {
    "use strict";

    var pager = document.querySelector("[data-quiz-pager]");
    if (!pager) {
        return;
    }

    var form = pager.closest("form");
    var pages = Array.prototype.slice.call(pager.querySelectorAll("[data-quiz-page]"));
    var nav = pager.querySelector("[data-pager-nav]");
    var navItems = Array.prototype.slice.call(pager.querySelectorAll("[data-pager-goto]"));
    var buttons = pager.querySelector("[data-pager-buttons]");
    var prev = pager.querySelector("[data-pager-prev]");
    var next = pager.querySelector("[data-pager-next]");
    var lastSubmit = buttons.querySelector("[data-pager-submit]");
    var position = pager.querySelector("[data-pager-position]");
    var storageKey = "sl-quiz-page:" + (form ? form.getAttribute("action") : location.pathname);
    var current = 0;

    function saved() {
        try {
            var value = Number(sessionStorage.getItem(storageKey));
            return value >= 0 && value < pages.length ? value : 0;
        } catch (e) {
            return 0;
        }
    }

    function save(index) {
        try {
            sessionStorage.setItem(storageKey, String(index));
        } catch (e) {
            // Private mode: the page simply starts at the first question next time
        }
    }

    // Whether the question on a screen has an answer: a choice, typed text, a picked match or changed code
    function isAnswered(page) {
        var fields = page.querySelectorAll("input, textarea, select");
        for (var i = 0; i < fields.length; i++) {
            var field = fields[i];
            if (!field.name || field.disabled || field.type === "hidden") {
                continue;
            }
            if (field.type === "radio" || field.type === "checkbox") {
                if (field.checked) {
                    return true;
                }
            } else if (field.tagName === "TEXTAREA" && field.hasAttribute("data-code-editor")) {
                if (field.value.trim() !== "" && field.value !== field.defaultValue) {
                    return true;
                }
            } else if (field.value.trim() !== "") {
                return true;
            }
        }
        return false;
    }

    function unanswered() {
        var numbers = [];
        pages.forEach(function (page, index) {
            if (!isAnswered(page)) {
                numbers.push(index + 1);
            }
        });
        return numbers;
    }

    function refreshNav() {
        navItems.forEach(function (item) {
            var index = Number(item.dataset.pagerGoto);
            var answered = isAnswered(pages[index]);
            var isCurrent = index === current;
            item.classList.toggle("sl-quiz-nav-answered", answered);
            item.classList.toggle("sl-quiz-nav-current", isCurrent);
            if (isCurrent) {
                item.setAttribute("aria-current", "step");
            } else {
                item.removeAttribute("aria-current");
            }
            item.setAttribute("aria-label", "Въпрос " + (index + 1) + (answered ? ", с отговор" : ", без отговор"));
        });
    }

    function hideWarnings() {
        pager.querySelectorAll("[data-pager-warning]").forEach(function (warning) {
            warning.hidden = true;
            warning.replaceChildren();
        });
    }

    function show(index, focus) {
        current = Math.max(0, Math.min(index, pages.length - 1));
        pages.forEach(function (page, i) {
            page.hidden = i !== current;
        });

        var last = current === pages.length - 1;
        prev.hidden = current === 0;
        next.hidden = last;
        lastSubmit.hidden = !last;
        position.textContent = "Въпрос " + (current + 1) + " от " + pages.length;

        hideWarnings();
        refreshNav();
        save(current);

        if (focus) {
            var legend = pages[current].querySelector("legend");
            var top = pager.getBoundingClientRect().top + window.scrollY - 16;
            if (window.scrollY > top) {
                window.scrollTo({ top: top });
            }
            if (legend) {
                legend.focus({ preventScroll: true });
            }
        }
    }

    function warn(area, numbers, button) {
        var warning = area.querySelector("[data-pager-warning]");
        warning.replaceChildren();
        var text = document.createElement("div");
        text.className = "mb-2";
        text.textContent = (numbers.length === 1 ? "Без отговор е въпрос " : "Без отговор са въпроси ")
            + numbers.join(", ") + ". Да предам ли все пак?";
        warning.appendChild(text);

        var actions = document.createElement("div");
        actions.className = "d-flex flex-wrap gap-2";
        var confirm = document.createElement("button");
        confirm.type = "submit";
        confirm.className = "btn btn-sm btn-success";
        confirm.textContent = "Предай все пак";
        confirm.dataset.pagerConfirmed = "true";
        var back = document.createElement("button");
        back.type = "button";
        back.className = "btn btn-sm btn-outline-secondary";
        back.textContent = "Към въпрос " + numbers[0];
        back.addEventListener("click", function () {
            show(numbers[0] - 1, true);
        });
        actions.appendChild(confirm);
        actions.appendChild(back);
        warning.appendChild(actions);
        warning.hidden = false;
        confirm.focus();
    }

    // Switch from "all questions" (the page without JavaScript) to one per screen
    pager.querySelector("[data-pager-fallback]").hidden = true;
    buttons.hidden = false;
    nav.hidden = false;
    position.hidden = false;

    prev.addEventListener("click", function () {
        show(current - 1, true);
    });
    next.addEventListener("click", function () {
        show(current + 1, true);
    });
    navItems.forEach(function (item) {
        item.addEventListener("click", function () {
            show(Number(item.dataset.pagerGoto), true);
        });
    });

    pager.addEventListener("click", function (event) {
        var button = event.target.closest("[data-pager-submit]");
        if (!button) {
            return;
        }
        var missing = unanswered();
        if (missing.length > 0) {
            event.preventDefault();
            warn(button.closest("[data-pager-submit-area]"), missing, button);
        }
    });

    // A new attempt starts at the first question
    form.addEventListener("submit", function () {
        try {
            sessionStorage.removeItem(storageKey);
        } catch (e) {
            // Nothing stored
        }
    });

    // The navigator follows the answers as they are given
    form.addEventListener("input", refreshNav);
    form.addEventListener("change", refreshNav);

    // An invalid field on a hidden screen would block the submit without showing why: open its screen first
    form.addEventListener("invalid", function (event) {
        var page = event.target.closest("[data-quiz-page]");
        if (page && page.hidden) {
            show(pages.indexOf(page), false);
        }
    }, true);

    show(saved(), false);
})();
