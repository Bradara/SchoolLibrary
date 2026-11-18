// Renders an uploaded EPUB inside the details page, using epub.js (and JSZip).
// The file is fetched from the Download endpoint of this site, so nothing leaves the server.
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

    function showError() {
        area.innerHTML =
            '<div class="alert alert-warning m-2">Неуспешно зареждане на книгата. Използвайте бутона за сваляне.</div>';
    }

    fetch(url)
        .then(function (response) {
            if (!response.ok) {
                throw new Error("HTTP " + response.status);
            }
            return response.arrayBuffer();
        })
        .then(function (buffer) {
            var book = ePub(buffer);
            var rendition = book.renderTo("sl-epub", {
                width: "100%",
                height: "100%",
                flow: "paginated"
            });
            rendition.display();

            var prev = document.getElementById("sl-epub-prev");
            var next = document.getElementById("sl-epub-next");
            if (prev) {
                prev.addEventListener("click", function () { rendition.prev(); });
            }
            if (next) {
                next.addEventListener("click", function () { rendition.next(); });
            }

            document.addEventListener("keyup", function (e) {
                if (e.key === "ArrowLeft") { rendition.prev(); }
                if (e.key === "ArrowRight") { rendition.next(); }
            });
        })
        .catch(showError);
})();
