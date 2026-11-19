// Counts down the exam timer and submits the form once when the time runs out.
// The server knows the start time and the limit, so this is only the visible clock and auto-submit.
(function () {
    "use strict";

    var form = document.getElementById("sl-exam-form");
    var display = document.getElementById("sl-exam-timer");
    if (!form || !display) {
        return;
    }

    var remaining = parseInt(form.getAttribute("data-remaining-seconds"), 10);
    if (isNaN(remaining)) {
        return;
    }

    var submitted = false;

    function pad(n) {
        return (n < 10 ? "0" : "") + n;
    }

    function render(seconds) {
        var minutes = Math.floor(seconds / 60);
        display.textContent = pad(minutes) + ":" + pad(seconds % 60);
    }

    function submitOnce() {
        if (submitted) {
            return;
        }
        submitted = true;
        if (typeof form.requestSubmit === "function") {
            form.requestSubmit();
        } else {
            form.submit();
        }
    }

    function tick() {
        if (remaining <= 0) {
            render(0);
            submitOnce();
            return;
        }

        render(remaining);
        remaining -= 1;
        window.setTimeout(tick, 1000);
    }

    tick();
})();
