// The cover of a PDF: its first page drawn by pdf.js (wwwroot/lib/pdfjs) in the browser, as a JPEG picture.
// A PDF carries no cover picture, and drawing it on the server would need a native library; the server only
// checks the picture it gets (type from the bytes, size). Also reads the title and the author of the document.
// Used by the upload form (window.SlPdfCover, for sl-epub-metadata.js) and by the administration page that
// makes the covers of the PDF files uploaded before (sl-pdf-covers-admin.js).

export const COVER_WIDTH = 400;            // pixels; the catalog card shows it at about 74 px (sharp on any screen)
export const MAX_COVER_BYTES = 300 * 1024; // as ResourceCovers.MaxPdfCoverBytes on the server
const QUALITIES = [0.82, 0.65, 0.5];

let loading = null;

function loadPdfJs(base) {
    if (!loading) {
        loading = import(base + "pdf.min.mjs").then((pdfjs) => {
            pdfjs.GlobalWorkerOptions.workerSrc = base + "pdf.worker.min.mjs";
            return pdfjs;
        });
    }
    return loading;
}

function toBlob(canvas, quality) {
    return new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", quality));
}

// A title that is only the name of the file the PDF was made from ("Microsoft Word - plan.docx") says nothing
function usefulTitle(title) {
    const text = (title || "").replace(/\s+/g, " ").trim();
    if (!text || /^(microsoft (word|powerpoint|excel)\s*-|untitled\b|unknown\b)/i.test(text)
        || /\.(docx?|odt|pptx?|odp|xlsx?|pdf|tex|rtf|indd)$/i.test(text)) {
        return "";
    }
    return text;
}

// The first page of the document as a JPEG of COVER_WIDTH pixels (a smaller quality when it is too large),
// or null when it cannot be drawn
async function drawFirstPage(pdfjs, pdf) {
    const page = await pdf.getPage(1);
    const size = page.getViewport({ scale: 1 });
    const viewport = page.getViewport({ scale: COVER_WIDTH / size.width });
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(viewport.width);
    canvas.height = Math.min(Math.round(viewport.height), COVER_WIDTH * 3); // a very long page is cut
    await page.render({ canvas, viewport }).promise;
    for (const quality of QUALITIES) {
        const blob = await toBlob(canvas, quality);
        if (blob && blob.size <= MAX_COVER_BYTES) {
            return blob;
        }
    }
    return null;
}

/**
 * Reads a PDF (a File, a Blob or a URL of this site): { cover: Blob|null, title, author }.
 * Nothing leaves the browser here.
 */
export async function readPdf(source, base) {
    const pdfjs = await loadPdfJs(base);
    const options = { cMapUrl: base + "cmaps/", cMapPacked: true, standardFontDataUrl: base + "standard_fonts/",
        wasmUrl: base + "wasm/", iccUrl: base + "iccs/", enableXfa: false };
    if (typeof source === "string") {
        options.url = source;
    } else {
        options.data = new Uint8Array(await source.arrayBuffer());
    }
    const loadingTask = pdfjs.getDocument(options);
    const pdf = await loadingTask.promise;
    try {
        let title = "";
        let author = "";
        try {
            const meta = await pdf.getMetadata();
            title = usefulTitle(meta && meta.info && meta.info.Title);
            author = ((meta && meta.info && meta.info.Author) || "").replace(/\s+/g, " ").trim();
        } catch {
            // without the metadata only the cover is made
        }
        return { cover: await drawFirstPage(pdfjs, pdf), title, author };
    } finally {
        loadingTask.destroy(); // frees the worker's copy of the document
    }
}

// The upload form: sl-epub-metadata.js asks for the cover and the metadata of a chosen PDF
const form = document.querySelector("[data-pdfjs-base]");
if (form && form.matches("form")) {
    window.SlPdfCover = { read: (file) => readPdf(file, form.dataset.pdfjsBase) };
    form.dispatchEvent(new CustomEvent("sl-pdf-cover-ready"));
}
