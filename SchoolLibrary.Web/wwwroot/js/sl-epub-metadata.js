// Upload form: when an EPUB is chosen, the title and the author are read from the book (in the browser, nothing is
// sent) and put into empty fields. A field the user typed in is never changed; a field filled from an earlier book
// is replaced by the next one. The server checks everything again on submit.
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
        var fileInput = form.querySelector('input[type="file"]');
        var note = form.querySelector('[data-epub-metadata-note]');
        var fields = {
            title: form.querySelector('[data-epub-field="title"]'),
            author: form.querySelector('[data-epub-field="author"]')
        };
        if (!fileInput || typeof JSZip === 'undefined') return;

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

        fileInput.addEventListener('change', async function () {
            if (note) note.hidden = true;
            var file = fileInput.files && fileInput.files[0];
            if (!file || !/\.epub$/i.test(file.name) || file.size > MAX_BYTES) return;

            try {
                var meta = await readMetadata(file);
                if (!meta) return;
                var any = fill('title', meta.title);
                any = fill('author', meta.author) || any;
                if (any && note) note.hidden = false;
            } catch (e) {
                // Not a readable book: the fields stay as they are, the server explains the problem on submit
            }
        });
    });
})();
