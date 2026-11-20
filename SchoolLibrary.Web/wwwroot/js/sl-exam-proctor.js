// Exam proctoring. The exam starts in fullscreen (a browser only allows that after a click, hence the
// start screen). Leaving fullscreen, the tab or the window shows a large red warning; for a proctored
// exam each leave is also recorded on the server, which decides when the limit is passed — then the
// attempt is submitted with a "forced submission" notice. A browser cannot block switching away, so
// this only detects and records it. The server is the source of truth.
(function () {
    "use strict";

    var form = document.getElementById("sl-exam-form");
    var content = document.getElementById("sl-exam-content");
    var startScreen = document.getElementById("sl-exam-start");
    var startButton = document.getElementById("sl-exam-start-btn");
    var warning = document.getElementById("sl-exam-warning");
    var warningText = document.getElementById("sl-exam-warning-text");
    var returnButton = document.getElementById("sl-exam-return-btn");
    var forced = document.getElementById("sl-exam-forced");
    var notice = document.getElementById("sl-proctor-notice");
    if (!form || !content || !startScreen || !startButton) {
        return;
    }

    var root = document.documentElement;
    var proctored = form.getAttribute("data-proctored") === "true";
    var maxLeaves = parseInt(form.getAttribute("data-max-leaves"), 10);
    var leaveCount = parseInt(form.getAttribute("data-leave-count"), 10) || 0;
    var leaveUrl = form.getAttribute("data-leave-url");
    var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
    var token = tokenInput ? tokenInput.value : "";

    var started = false;
    var submitting = false;
    var lastLeave = 0;

    // ---------- Fullscreen (standard API, with the WebKit prefix for older Safari) ----------

    var canFullscreen = !!(root.requestFullscreen || root.webkitRequestFullscreen);

    function isFullscreen() {
        return !!(document.fullscreenElement || document.webkitFullscreenElement);
    }

    function enterFullscreen() {
        try {
            var request = root.requestFullscreen
                ? root.requestFullscreen()
                : (root.webkitRequestFullscreen ? root.webkitRequestFullscreen() : null);
            if (request && typeof request.catch === "function") {
                request.catch(function () { /* refused: the exam still runs, leaves are still watched */ });
            }
        } catch (e) { /* same as refused */ }
    }

    if (!canFullscreen) {
        var hint = document.getElementById("sl-exam-no-fullscreen");
        if (hint) {
            hint.classList.remove("d-none");
        }
    }

    // ---------- Screens ----------

    function show(element) {
        if (element) {
            element.classList.remove("d-none");
        }
    }

    function hide(element) {
        if (element) {
            element.classList.add("d-none");
        }
    }

    function leaveMessage() {
        if (!proctored || isNaN(maxLeaves)) {
            return "Излезе от изпита. Върни се и не напускай страницата до предаването.";
        }

        var left = maxLeaves - leaveCount;
        var text = "Напускания: " + leaveCount + " от " + maxLeaves + " разрешени. Опитът се записва.";
        if (left <= 0) {
            text += " При следващо напускане изпитът ще бъде предаден принудително.";
        }
        return text;
    }

    function showWarning() {
        if (warningText) {
            warningText.textContent = leaveMessage();
        }
        show(warning);
        if (notice && proctored) {
            notice.textContent = "Напусна изпита " + leaveCount + " път(и). Това се записва.";
            show(notice);
        }
    }

    startButton.addEventListener("click", function () {
        if (canFullscreen) {
            enterFullscreen();
        }
        started = true;
        hide(startScreen);
        show(content);
    });

    if (returnButton) {
        returnButton.addEventListener("click", function () {
            if (canFullscreen && !isFullscreen()) {
                enterFullscreen();
            }
            hide(warning);
        });
    }

    // ---------- Submitting ----------

    // Any submit (the button, the timer or a forced one) ends the watching, so the page change that
    // follows (which also leaves fullscreen) is not taken for a leave
    form.addEventListener("submit", function () {
        submitting = true;
    });

    function forceSubmit() {
        if (submitting) {
            return;
        }
        submitting = true;
        hide(warning);
        show(forced);
        // Give the student a moment to read the notice, then hand in the answers given so far
        window.setTimeout(function () {
            if (typeof form.requestSubmit === "function") {
                form.requestSubmit();
            } else {
                form.submit();
            }
        }, 1500);
    }

    // ---------- Leaves ----------

    function onLeave() {
        if (!started || submitting) {
            return;
        }

        // Leaving fullscreen often also blurs the window; count one leave per second at most
        var now = Date.now();
        if (now - lastLeave < 1000) {
            return;
        }
        lastLeave = now;

        if (!proctored) {
            showWarning();
            return;
        }

        leaveCount += 1;
        showWarning();

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
                leaveCount = data.leaveCount;
                if (data.shouldSubmit) {
                    forceSubmit();
                } else {
                    showWarning();
                }
            })
            .catch(function () { /* a failed ping must not block the exam */ });
    }

    function onFullscreenChange() {
        if (!isFullscreen()) {
            onLeave();
        }
    }

    document.addEventListener("fullscreenchange", onFullscreenChange);
    document.addEventListener("webkitfullscreenchange", onFullscreenChange);
    document.addEventListener("visibilitychange", function () {
        if (document.hidden) {
            onLeave();
        }
    });
    window.addEventListener("blur", onLeave);
})();
