// Upload form: when an EPUB or a PDF is chosen, the title and the author are read from the file (in the browser) and
// put into empty fields. A field the user typed in is never changed; a field filled from an earlier file is replaced
// by the next one. For a PDF the first page is drawn as the cover (sl-pdf-cover.js) and sent with the form in a
// hidden file field. The server checks everything again on submit.
(function () {
    'use strict';

    var OPF = 'http://www.idpf.org/2007/opf';
    var DC = 'http://purl.org/dc/elements/1.1/';
    var MAX_BYTES = 25 * 1024 * 1024;

    function oneLine(text, maxLength) {
        var line = (text || '').replace(/[\s\u0000-\u001f]+/g, ' ').trim();
        return maxLength > 0 && line.length > maxLength ? line.slice(0, maxLength - 1) + '…' : line;
    }

    function parseXml(text) {
        var doc = new DOMParser().parseFromString(text, 'application/xml');
        return doc.getElementsByTagName('parsererror').length ? null : doc;
    }

    // The path of a file inside the ZIP, relative to the package document
    function resolve(base, href) {
        var parts = base.split('/').slice(0, -1);
        href.split('/').forEach(function (part) {
            if (part === '..') parts.pop();
            else if (part && part !== '.') parts.push(part);
        });
        return parts.join('/');
    }

    async function readMetadata(file) {
        var zip = await JSZip.loadAsync(file);
        var container = zip.file('META-INF/container.xml');
        if (!container) return null;

        var containerDoc = parseXml(await container.async('string'));
        var rootfile = containerDoc && containerDoc.getElementsByTagName('rootfile')[0];
        var opfPath = rootfile && rootfile.getAttribute('full-path');
        var opf = opfPath && zip.file(resolve('', opfPath));
        if (!opf) return null;

        var doc = parseXml(await opf.async('string'));
        if (!doc) return null;

        var metadata = doc.getElementsByTagNameNS(OPF, 'metadata')[0] || doc;
        var titles = metadata.getElementsByTagNameNS(DC, 'title');
        var creators = Array.prototype.map.call(metadata.getElementsByTagNameNS(DC, 'creator'), function (c) {
            return oneLine(c.textContent, 0);
        }).filter(function (name, i, all) { return name && all.indexOf(name) === i; });

        return {
            title: titles.length ? oneLine(titles[0].textContent, 0) : '',
            author: creators.join(', ')
        };
    }

    document.querySelectorAll('[data-epub-metadata]').forEach(function (form) {
        var fileInput = form.querySelector('input[type="file"]:not([name="Cover"])');
        var coverInput = form.querySelector('input[type="file"][name="Cover"]');
        var preview = form.querySelector('[data-pdf-cover-preview]');
        var note = form.querySelector('[data-epub-metadata-note]');
        var fields = {
            title: form.querySelector('[data-epub-field="title"]'),
            author: form.querySelector('[data-epub-field="author"]')
        };
        if (!fileInput) return;
        var previewUrl = null;
        var chosen = 0; // a later choice wins over a PDF still being read

        // What the script put into each field; a field whose value still equals it may be replaced
        var filled = { title: null, author: null };

        function fill(name, value) {
            var input = fields[name];
            if (!input || !value) return false;
            var current = input.value.trim();
            if (current !== '' && current !== filled[name]) return false;

            var max = parseInt(input.getAttribute('maxlength') || input.getAttribute('data-val-maxlength-max') || '0', 10);
            input.value = oneLine(value, max);
            filled[name] = input.value;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            return true;
        }

        function setCover(blob) {
            if (previewUrl) {
                URL.revokeObjectURL(previewUrl);
                previewUrl = null;
            }
            if (coverInput) {
                var files = new DataTransfer();
                if (blob) files.items.add(new File([blob], 'cover.jpg', { type: 'image/jpeg' }));
                coverInput.files = files.files;
            }
            if (preview) {
                preview.hidden = !blob;
                if (blob) {
                    previewUrl = URL.createObjectURL(blob);
                    preview.querySelector('img').src = previewUrl;
                }
            }
        }

        async function readPdf(file, mine) {
            if (!window.SlPdfCover) return;
            try {
                var pdf = await window.SlPdfCover.read(file);
                if (mine !== chosen) return;
                setCover(pdf.cover);
                var any = fill('title', pdf.title);
                any = fill('author', pdf.author) || any;
                if (any && note) note.hidden = false;
            } catch (e) {
                // Not a readable PDF: no cover, the server explains the problem on submit
            }
        }

        fileInput.addEventListener('change', async function () {
            if (note) note.hidden = true;
            var mine = ++chosen;
            setCover(null);
            var file = fileInput.files && fileInput.files[0];
            if (!file || file.size > MAX_BYTES) return;
            if (/\.pdf$/i.test(file.name)) {
                await readPdf(file, mine);
                return;
            }
            if (!/\.epub$/i.test(file.name) || typeof JSZip === 'undefined') return;

            try {
                var meta = await readMetadata(file);
                if (!meta || mine !== chosen) return;
                var any = fill('title', meta.title);
                any = fill('author', meta.author) || any;
                if (any && note) note.hidden = false;
            } catch (e) {
                // Not a readable book: the fields stay as they are, the server explains the problem on submit
            }
        });
    });
})();
