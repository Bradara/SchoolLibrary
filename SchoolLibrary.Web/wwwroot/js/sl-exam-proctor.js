// Light exam proctoring: a fullscreen button and a count of how many times the student leaves the
// exam tab/window. A browser cannot block tab switching, so this only records leaves on the server
// and, once the exam's limit is passed, submits the attempt. The server is the source of truth.
(function () {
    "use strict";

    var form = document.getElementById("sl-exam-form");
    if (!form) {
        return;
    }

    // Fullscreen toggle (works with or without proctoring)
    var fullscreenButton = document.getElementById("sl-fullscreen-btn");
    if (fullscreenButton) {
        fullscreenButton.addEventListener("click", function () {
            if (!document.fullscreenElement && document.documentElement.requestFullscreen) {
                document.documentElement.requestFullscreen().catch(function () { });
            } else if (document.exitFullscreen) {
                document.exitFullscreen().catch(function () { });
            }
        });
    }

    if (form.getAttribute("data-proctored") !== "true") {
        return;
    }

    var leaveUrl = form.getAttribute("data-leave-url");
    var notice = document.getElementById("sl-proctor-notice");
    var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
    var token = tokenInput ? tokenInput.value : "";

    var submitted = false;
    var lastLeave = 0;

    function warn(count) {
        if (notice) {
            notice.textContent = "Напусна изпита " + count + " път(и). Това се записва.";
            notice.classList.remove("d-none");
        }
    }

    function autoSubmit() {
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

    function recordLeave() {
        // A tab switch fires both blur and visibilitychange; count it once
        var now = Date.now();
        if (now - lastLeave < 1000) {
            return;
        }
        lastLeave = now;

        fetch(leaveUrl, {
            method: "POST",
            headers: { "RequestVerificationToken": token },
            credentials: "same-origin"
        })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (data) {
                if (!data) {
                    return;
                }
                warn(data.leaveCount);
                if (data.shouldSubmit) {
                    autoSubmit();
                }
            })
            .catch(function () { /* a failed ping must not block the exam */ });
    }

    document.addEventListener("visibilitychange", function () {
        if (document.hidden) {
            recordLeave();
        }
    });
    window.addEventListener("blur", recordLeave);
})();
