// Maximizes a preview container (.sl-viewer) to a full-window overlay and back.
// Works for any content inside .sl-viewer-body: a PDF iframe, the EPUB reader or rendered Markdown.
(function () {
    "use strict";

    function toggle(viewer) {
        var on = viewer.classList.toggle("sl-maximized");
        document.body.classList.toggle("sl-maximized-open", on);

        var btn = viewer.querySelector(".sl-viewer-toggle");
        if (btn) {
            btn.setAttribute("aria-pressed", on ? "true" : "false");
            btn.setAttribute("aria-label", on ? "Изход от цял екран" : "На цял екран");
            var icon = btn.querySelector("i");
            if (icon) {
                icon.className = on ? "bi bi-fullscreen-exit" : "bi bi-arrows-fullscreen";
            }
        }

        // Let a viewer that measures its own box (epub.js) re-layout to the new size
        window.dispatchEvent(new Event("resize"));
    }

    document.addEventListener("click", function (e) {
        var btn = e.target.closest(".sl-viewer-toggle");
        if (!btn) {
            return;
        }
        var viewer = btn.closest(".sl-viewer");
        if (viewer) {
            toggle(viewer);
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            var open = document.querySelector(".sl-viewer.sl-maximized");
            if (open) {
                toggle(open);
            }
        }
    });
})();
