// Renders an uploaded PDF inside the details page, using pdf.js (wwwroot/lib/pdfjs, loaded only here).
// The file is fetched from the Download endpoint of this site. The pages are drawn one under another, only
// the ones near the screen (a long document does not fill the memory); each page gets a text layer, so the
// text can be selected and copied. Zoom (page width, whole page, percent) and dark pages are kept in this
// browser. The page the reader stopped at is kept on the server for a signed-in user (any device) and in this
// browser for a visitor. Unlike the PDF viewer of the browser in a frame, this works the same on phones.
const stage = document.getElementById("sl-pdf");

const ZOOM_KEY = "sl-pdf-zoom";
const DARK_KEY = "sl-pdf-dark";
const ZOOM_STEPS = [0.5, 0.75, 1, 1.25, 1.5, 2, 3];
const MIN_SCALE = 0.25;
const MAX_SCALE = 4;
const MAX_RENDERED = 8;           // pages kept drawn; the others are drawn again when they come near
const MAX_CANVAS_PIXELS = 8e6;    // a sharper picture than this costs too much memory (phones)
const PAGE_GAP = 12;              // the space around the pages (as in the CSS)
// Dark pages: pdf.js draws the text and the lines in these colors; pictures stay as they are
const DARK_COLORS = { background: "#1e1f22", foreground: "#e3e3e3" };
const SAVE_DELAY = 1500;          // the page is sent to the server when the reader stays on it this long

function load(key, fallback) {
    try {
        const value = localStorage.getItem(key);
        return value === null ? fallback : value;
    } catch {
        return fallback;
    }
}

function save(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // the reader works without the saved value
    }
}

function el(id) {
    return document.getElementById(id);
}

function showMessage(text, withLink) {
    const message = el("sl-pdf-message") || document.createElement("p");
    message.id = "sl-pdf-message";
    message.className = "sl-pdf-message text-body-secondary p-3 mb-0";
    message.textContent = text;
    if (withLink) {
        const link = document.createElement("a");
        link.href = stage.dataset.pdfUrl;
        link.className = "ms-1";
        link.textContent = "Отворете файла";
        message.append(" ", link, ".");
    }
    if (!message.isConnected) {
        stage.prepend(message);
    }
}

// "pdfpage(12)" → 12 (the same shape as PdfLocation on the server); 0 when it is not a page
function pageOf(location) {
    const match = /^pdfpage\(([1-9][0-9]{0,5})\)$/.exec(location || "");
    return match ? parseInt(match[1], 10) : 0;
}

function antiforgeryToken() {
    const input = document.querySelector('#sl-pdf-token input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
}

// --- the page the reader is on: on the server for a signed-in user, in this browser for a visitor ---
function createSync(pageCount) {
    const saveUrl = stage.dataset.pdfSaveUrl || "";
    const key = stage.dataset.pdfKey;
    const serverLocation = stage.dataset.pdfPosition || "";
    const status = el("sl-pdf-sync");

    let savedLocation = serverLocation;
    let pending = null;
    let timer = 0;

    function showStatus(ok) {
        if (!status) {
            return;
        }
        const icon = document.createElement("i");
        icon.className = ok ? "bi bi-cloud-check" : "bi bi-cloud-slash text-warning";
        status.title = ok
            ? "Докъдето сте стигнали, се пази в профила ви — продължете от всяко устройство."
            : "Страницата не можа да се запази в профила ви. Ще опитаме отново.";
        status.replaceChildren(icon);
    }

    function send(keepalive) {
        clearTimeout(timer);
        if (!pending || !saveUrl || !antiforgeryToken() || pending.location === savedLocation) {
            return;
        }
        const position = pending;
        const body = new FormData();
        body.append("location", position.location);
        body.append("percent", String(position.percent));
        body.append("__RequestVerificationToken", antiforgeryToken());

        fetch(saveUrl, { method: "POST", body, credentials: "same-origin", keepalive: !!keepalive })
            .then((response) => {
                if (response.ok) {
                    savedLocation = position.location;
                    if (pending === position) {
                        pending = null;
                    }
                }
                showStatus(response.ok);
            })
            .catch(() => showStatus(false));
    }

    if (saveUrl) {
        showStatus(true);
        // Leaving the page (or switching to another app on a phone) saves at once
        document.addEventListener("visibilitychange", () => {
            if (document.visibilityState === "hidden") {
                send(true);
            }
        });
        window.addEventListener("pagehide", () => send(true));
    }

    return {
        // The server page wins (it may come from another device); a page kept in this browser before is
        // used once and then moved to the server
        startPage() {
            const page = pageOf(serverLocation) || pageOf(load(key, ""));
            return Math.min(pageCount, Math.max(1, page || 1));
        },
        moved(page) {
            const location = "pdfpage(" + page + ")";
            save(key, location);
            if (saveUrl) {
                pending = { location, percent: Math.round(page / pageCount * 100) };
                clearTimeout(timer);
                timer = setTimeout(() => send(false), SAVE_DELAY);
            }
        }
    };
}

function errorText(error) {
    switch (error && error.name) {
        case "PasswordException":
            return "Документът е защитен с парола и не може да се покаже тук.";
        case "InvalidPDFException":
            return "Файлът не е валиден PDF документ.";
        default:
            return "Документът не можа да се покаже.";
    }
}

async function start() {
    const base = stage.dataset.pdfjsBase;
    const pdfjs = await import(base + "pdf.min.mjs");
    pdfjs.GlobalWorkerOptions.workerSrc = base + "pdf.worker.min.mjs";

    const pdf = await pdfjs.getDocument({
        url: stage.dataset.pdfUrl,
        cMapUrl: base + "cmaps/",
        cMapPacked: true,
        standardFontDataUrl: base + "standard_fonts/",
        wasmUrl: base + "wasm/",
        iccUrl: base + "iccs/",
        enableXfa: false
    }).promise;

    const firstPage = await pdf.getPage(1);
    const firstSize = firstPage.getViewport({ scale: 1 });
    createReader(pdfjs, pdf, firstSize.width, firstSize.height);
}

function createReader(pdfjs, pdf, defaultWidth, defaultHeight) {
    const pageInput = el("sl-pdf-page");
    const countLabel = el("sl-pdf-count");
    const prevButton = el("sl-pdf-prev");
    const nextButton = el("sl-pdf-next");
    const zoomSelect = el("sl-pdf-zoom");
    const zoomOut = el("sl-pdf-zoom-out");
    const zoomIn = el("sl-pdf-zoom-in");
    const darkButton = el("sl-pdf-dark");

    const pageCount = pdf.numPages;
    let mode = load(ZOOM_KEY, "width");   // "width", "page" or a number
    if (mode !== "width" && mode !== "page" && !(parseFloat(mode) > 0)) {
        mode = "width";
    }
    let scale = 1;
    let dark = load(DARK_KEY, "0") === "1";
    let current = 1;
    let restored = false;          // the saved page is open: from now on every new page is saved
    const rendered = new Set();
    const drawnListeners = [];     // called with the page number when a page (and its text layer) is drawn
    const sync = createSync(pageCount);

    // One box per page, sized before the page is read (every page as big as the first one, corrected
    // when the page itself is read), so that the scroll bar and "go to page" work from the start.
    const slots = [];
    const fragment = document.createDocumentFragment();
    for (let number = 1; number <= pageCount; number++) {
        const slot = document.createElement("div");
        slot.className = "sl-pdf-page";
        slot.setAttribute("role", "img");
        slot.setAttribute("aria-label", "Страница " + number + " от " + pageCount);
        slot.dataset.number = String(number);
        slots.push({ element: slot, number, width: defaultWidth, height: defaultHeight, page: null,
            token: 0, task: null, drawnScale: 0, drawnDark: false });
        fragment.append(slot);
    }
    const message = el("sl-pdf-message");
    if (message) {
        message.remove();
    }
    stage.append(fragment);

    function clampScale(value) {
        return Math.min(MAX_SCALE, Math.max(MIN_SCALE, value));
    }

    function fitScale() {
        const reference = slots[current - 1];
        const width = stage.clientWidth - 2 * PAGE_GAP;
        if (mode === "width") {
            return clampScale(width / reference.width);
        }
        const height = stage.clientHeight - 2 * PAGE_GAP;
        return clampScale(Math.min(width / reference.width, height / reference.height));
    }

    function sizeSlot(slot) {
        const style = slot.element.style;
        style.width = Math.floor(slot.width * scale) + "px";
        style.height = Math.floor(slot.height * scale) + "px";
        // used by the text layer of pdf.js
        style.setProperty("--total-scale-factor", String(scale));
        style.setProperty("--scale-factor", String(scale));
        style.setProperty("--scale-round-x", "1px");
        style.setProperty("--scale-round-y", "1px");
    }

    // A new scale keeps the same place: the same page and the same part of it on top of the screen
    function layout() {
        const anchor = slots[current - 1].element;
        const within = anchor.offsetHeight ? (stage.scrollTop - anchor.offsetTop) / anchor.offsetHeight : 0;
        const numeric = parseFloat(mode);
        scale = isFinite(numeric) ? clampScale(numeric) : fitScale();
        slots.forEach(sizeSlot);
        stage.scrollTop = anchor.offsetTop + within * anchor.offsetHeight;
        update();
    }

    // The pages between the top and the bottom of the screen
    function visibleRange() {
        const top = stage.scrollTop;
        const bottom = top + stage.clientHeight;
        let low = 0;
        let high = slots.length - 1;
        while (low < high) {
            const middle = (low + high) >> 1;
            const element = slots[middle].element;
            if (element.offsetTop + element.offsetHeight < top) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }
        let last = low;
        while (last + 1 < slots.length && slots[last + 1].element.offsetTop < bottom) {
            last++;
        }
        return { first: low, last };
    }

    // The current page is the one at a third of the screen (the last one at the end of the document)
    function findCurrent() {
        if (stage.scrollTop + stage.clientHeight >= stage.scrollHeight - 2) {
            return pageCount;
        }
        const line = stage.scrollTop + stage.clientHeight / 3;
        const range = visibleRange();
        for (let i = range.first; i <= range.last; i++) {
            const element = slots[i].element;
            if (element.offsetTop + element.offsetHeight + PAGE_GAP > line) {
                return i + 1;
            }
        }
        return range.first + 1;
    }

    function update() {
        const range = visibleRange();
        for (let i = Math.max(0, range.first - 1); i <= Math.min(slots.length - 1, range.last + 1); i++) {
            draw(slots[i]);
        }
        setCurrent(findCurrent());
    }

    function setCurrent(number) {
        const changed = number !== current;
        current = number;
        if (restored && changed) {
            sync.moved(number);
        }
        if (document.activeElement !== pageInput) {
            pageInput.value = String(number);
        }
        prevButton.disabled = number <= 1;
        nextButton.disabled = number >= pageCount;
    }

    function goTo(number) {
        const target = Math.min(pageCount, Math.max(1, number | 0));
        stage.scrollTop = slots[target - 1].element.offsetTop - PAGE_GAP / 2;
        setCurrent(target);
        update();
    }

    async function draw(slot) {
        if (slot.drawnScale === scale && slot.drawnDark === dark) {
            return;
        }
        if (slot.task) {
            slot.task.cancel();
            slot.task = null;
        }
        const token = ++slot.token;
        try {
            if (!slot.page) {
                slot.page = await pdf.getPage(slot.number);
                const size = slot.page.getViewport({ scale: 1 });
                if (Math.abs(size.width - slot.width) > 0.5 || Math.abs(size.height - slot.height) > 0.5) {
                    // a page of another size: keep the place on the screen while the box changes
                    const before = stage.scrollTop;
                    const above = slot.element.offsetTop < before;
                    const oldHeight = slot.element.offsetHeight;
                    slot.width = size.width;
                    slot.height = size.height;
                    sizeSlot(slot);
                    if (above) {
                        stage.scrollTop = before + slot.element.offsetHeight - oldHeight;
                    }
                }
            }
            if (token !== slot.token) {
                return;
            }
            const viewport = slot.page.getViewport({ scale });
            let ratio = Math.min(window.devicePixelRatio || 1, 2);
            if (viewport.width * viewport.height * ratio * ratio > MAX_CANVAS_PIXELS) {
                ratio = Math.sqrt(MAX_CANVAS_PIXELS / (viewport.width * viewport.height));
            }
            const canvas = document.createElement("canvas");
            canvas.className = "sl-pdf-canvas";
            canvas.width = Math.max(1, Math.floor(viewport.width * ratio));
            canvas.height = Math.max(1, Math.floor(viewport.height * ratio));
            const drawnDark = dark;
            slot.task = slot.page.render({
                canvas,
                viewport,
                transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : null,
                pageColors: drawnDark ? DARK_COLORS : null
            });
            await slot.task.promise;
            slot.task = null;
            if (token !== slot.token) {
                return;
            }

            const textContent = await textOf(slot);
            if (token !== slot.token) {
                return;
            }
            const textDiv = document.createElement("div");
            textDiv.className = "textLayer";
            const textLayer = new pdfjs.TextLayer({ textContentSource: textContent, container: textDiv, viewport });
            await textLayer.render();
            if (token !== slot.token) {
                return;
            }

            // the old picture stays (stretched) until the new one is ready, so a zoom does not blink
            slot.element.replaceChildren(canvas, textDiv);
            slot.element.classList.toggle("is-dark", drawnDark);
            slot.drawnScale = scale;
            slot.drawnDark = drawnDark;
            // one span for each text item of the page, in the same order (read aloud marks the sentence in them)
            slot.textDivs = textLayer.textDivs;
            rendered.add(slot);
            releaseFarPages();
            drawnListeners.forEach((listener) => listener(slot.number));
        } catch (error) {
            if (error && error.name === "RenderingCancelledException") {
                return;
            }
            slot.task = null;
            slot.element.classList.add("has-error");
            slot.element.setAttribute("aria-label", "Страница " + slot.number + " не можа да се покаже");
        }
    }

    // The text of a page (read once and kept: the text layer and read aloud both use it)
    async function textOf(slot) {
        if (!slot.text) {
            if (!slot.page) {
                slot.page = await pdf.getPage(slot.number);
            }
            slot.text = await slot.page.getTextContent();
        }
        return slot.text;
    }

    // Only the pages closest to the current one stay drawn
    function releaseFarPages() {
        if (rendered.size <= MAX_RENDERED) {
            return;
        }
        const far = [...rendered].sort((a, b) => Math.abs(b.number - current) - Math.abs(a.number - current));
        for (const slot of far.slice(0, rendered.size - MAX_RENDERED)) {
            slot.token++;
            if (slot.task) {
                slot.task.cancel();
                slot.task = null;
            }
            slot.element.replaceChildren();
            slot.textDivs = null;
            slot.drawnScale = 0;
            rendered.delete(slot);
        }
    }

    // --- toolbar ---

    function showZoom() {
        const numeric = parseFloat(mode);
        const value = isFinite(numeric) ? String(numeric) : mode;
        if (![...zoomSelect.options].some((option) => option.value === value)) {
            const option = new Option(Math.round(numeric * 100) + "%", value);
            option.dataset.custom = "true";
            zoomSelect.querySelectorAll("option[data-custom]").forEach((old) => old.remove());
            zoomSelect.append(option);
        }
        zoomSelect.value = value;
    }

    function setMode(value) {
        mode = value;
        save(ZOOM_KEY, value);
        showZoom();
        layout();
    }

    function step(direction) {
        const next = direction > 0
            ? ZOOM_STEPS.find((value) => value > scale + 0.001)
            : [...ZOOM_STEPS].reverse().find((value) => value < scale - 0.001);
        if (next !== undefined) {
            setMode(String(next));
        }
    }

    function showDark() {
        darkButton.setAttribute("aria-pressed", dark ? "true" : "false");
        darkButton.classList.toggle("active", dark);
        stage.classList.toggle("is-dark", dark);
    }

    countLabel.textContent = "/ " + pageCount;
    pageInput.max = String(pageCount);
    [pageInput, prevButton, nextButton, zoomSelect, zoomOut, zoomIn, darkButton].forEach((control) => {
        control.disabled = false;
    });

    // A page chosen by the reader (not the scrolling) is told to read aloud, which stops there
    function jump(number) {
        goTo(number);
        stage.dispatchEvent(new CustomEvent("sl-pdf-jump", { detail: { page: current } }));
    }

    prevButton.addEventListener("click", () => jump(current - 1));
    nextButton.addEventListener("click", () => jump(current + 1));
    pageInput.addEventListener("change", () => jump(parseInt(pageInput.value, 10) || current));
    pageInput.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
            e.preventDefault();
            jump(parseInt(pageInput.value, 10) || current);
        }
    });
    pageInput.addEventListener("blur", () => { pageInput.value = String(current); });
    zoomSelect.addEventListener("change", () => setMode(zoomSelect.value));
    zoomOut.addEventListener("click", () => step(-1));
    zoomIn.addEventListener("click", () => step(1));
    darkButton.addEventListener("click", () => {
        dark = !dark;
        save(DARK_KEY, dark ? "1" : "0");
        showDark();
        update();
    });

    // Left/right arrows turn the page (as in the EPUB reader); up/down scroll as usual
    document.addEventListener("keydown", (e) => {
        const target = e.target;
        if (target !== document.body && !stage.contains(target)) {
            return;
        }
        if (e.key === "ArrowLeft") {
            jump(current - 1);
        } else if (e.key === "ArrowRight") {
            jump(current + 1);
        }
    });

    let scrollFrame = 0;
    stage.addEventListener("scroll", () => {
        if (!scrollFrame) {
            scrollFrame = requestAnimationFrame(() => {
                scrollFrame = 0;
                update();
            });
        }
    }, { passive: true });

    // The fit modes follow the size of the reader (window, phone turned, full screen)
    let lastWidth = stage.clientWidth;
    let lastHeight = stage.clientHeight;
    let resizeTimer = 0;
    new ResizeObserver(() => {
        if (stage.clientWidth === lastWidth && stage.clientHeight === lastHeight) {
            return;
        }
        lastWidth = stage.clientWidth;
        lastHeight = stage.clientHeight;
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (!isFinite(parseFloat(mode))) {
                layout();
            } else {
                update();
            }
        }, 150);
    }).observe(stage);

    showZoom();
    showDark();
    layout();

    // Continue from the saved page
    const startPage = sync.startPage();
    if (startPage > 1) {
        goTo(startPage);
    }
    restored = true;
    if (startPage > 1) {
        sync.moved(startPage);   // a page from this browser moves to the server (nothing is sent when it is there)
    }

    stage.dispatchEvent(new CustomEvent("sl-pdf-ready", {
        detail: {
            pdf,
            pdfjs,
            pageCount,
            stage,
            goTo,
            currentPage: () => current,
            // read aloud: the text items of a page, its spans when it is drawn, the page box
            textOf: (number) => textOf(slots[number - 1]),
            textDivsOf: (number) => slots[number - 1].textDivs || null,
            pageElement: (number) => slots[number - 1].element,
            onDrawn: (listener) => drawnListeners.push(listener),
            load,
            save,
            positionKey: stage.dataset.pdfKey
        }
    }));
}

if (stage) {
    start().catch((error) => showMessage(errorText(error), true));
}
