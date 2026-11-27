// Administration → "Обложки на PDF файловете": draws the first page of every PDF without a cover in this browser
// (sl-pdf-cover.js, pdf.js) and sends each picture to the server, one document at a time. The server checks the
// picture again (type from the bytes, size) and refuses a PDF that already has a cover.
import { readPdf } from "./sl-pdf-cover.js";

const area = document.getElementById("sl-pdf-covers");

function token() {
    const input = document.querySelector('#sl-pdf-covers-token input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
}

function saveUrl(id) {
    // the address is made by the server with id 0, which is replaced here
    return area.dataset.saveUrl.replace(/\/0(?=$|\?)/, "/" + encodeURIComponent(id));
}

async function makeCover(item, base) {
    const result = item.querySelector("[data-pdf-cover-result]");
    result.className = "small text-muted";
    result.textContent = "Рисува се…";
    try {
        const pdf = await readPdf(item.dataset.url, base);
        if (!pdf.cover) {
            throw new Error("Първата страница не можа да се нарисува.");
        }

        const body = new FormData();
        body.append("picture", pdf.cover, "cover.jpg");
        body.append("__RequestVerificationToken", token());
        const response = await fetch(saveUrl(item.dataset.id), { method: "POST", body, credentials: "same-origin" });
        const answer = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(answer.message || "Сървърът не прие обложката.");
        }

        const img = item.querySelector("img");
        img.src = URL.createObjectURL(pdf.cover);
        img.classList.remove("d-none");
        result.className = "small text-success";
        result.textContent = "Готово";
        return true;
    } catch (error) {
        result.className = "small text-danger";
        result.textContent = error && error.name === "PasswordException"
            ? "Защитен с парола"
            : (error && error.message) || "Грешка";
        return false;
    }
}

if (area) {
    const start = document.getElementById("sl-pdf-covers-start");
    const status = document.getElementById("sl-pdf-covers-status");
    const items = [...area.querySelectorAll("[data-pdf-cover-item]")];
    start.disabled = false;

    start.addEventListener("click", async () => {
        start.disabled = true;
        let done = 0;
        let failed = 0;
        for (const item of items) {
            if (item.dataset.done === "true") {
                continue;
            }
            status.textContent = `${done + failed + 1} от ${items.length}…`;
            if (await makeCover(item, area.dataset.pdfjsBase)) {
                item.dataset.done = "true";
                done++;
            } else {
                failed++;
            }
        }
        status.textContent = failed
            ? `Готови: ${done}. Неуспешни: ${failed} (може да опитате пак).`
            : `Готови: ${done}.`;
        start.disabled = failed === 0;
    });
}
