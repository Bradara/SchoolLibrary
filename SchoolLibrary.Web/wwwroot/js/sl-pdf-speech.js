// Read aloud in the PDF reader. Waits for sl-pdf-reader.js to open the document ("sl-pdf-ready"); the buttons,
// the voices and the queue are in sl-speech.js. This file gives it the text: the text items of pdf.js page by
// page (a word cut with a hyphen at the end of a line is joined again, a sentence goes on over the next page),
// the mark on the sentence being read (in the spans of the text layer) and the scrolling that follows it.
// A scanned document has no text, so there is nothing to read.
(function () {
    "use strict";

    var HIGHLIGHT_NAME = "sl-speech";
    var MAX_EMPTY_PAGES = 20;    // pages without text (pictures) skipped one after another
    var BLOCK_GAP = 1.8;         // a gap of this many line heights between two lines starts a new paragraph
    var FOLLOW_AT = 0.25;        // the sentence brought on the screen is put at a quarter of its height

    if (typeof module === "object" && module.exports) {
        module.exports = { pagePieces: pagePieces, pageStart: pageStart, isPageNumber: isPageNumber };
        return;
    }

    var stage = document.getElementById("sl-pdf");
    var speech = window.SlSpeech;
    if (!stage || !speech || !speech.supported()) {
        return;
    }

    stage.addEventListener("sl-pdf-ready", function (event) {
        start(event.detail);
    });

    // The text items of a page: pdf.js makes one span in the text layer for each item with a "str", in the
    // same order, so the number of an item is also the number of its span
    function textItems(content) {
        return ((content && content.items) || []).filter(function (item) {
            return typeof item.str === "string";
        });
    }

    function startsWithLowercase(text) {
        return /^\p{Ll}/u.test(text || "");
    }

    // The pieces of a page for splitSentences, from an item and an offset in it. A piece is one item:
    // { node: { page, item, base }, text, block }, where base is where the text starts in the item.
    // The end of a line (and of the page) becomes a space, unless the line ends with a hyphen and the next one
    // goes on with a small letter: then the two parts of the word are joined. nextPageText is the text the next
    // page starts with ("" for the last page).
    function pagePieces(page, items, fromItem, fromOffset, blocks, nextPageText) {
        var pieces = [];
        var last = null;
        var lineEnded = false;   // the last piece already ends its line (a space added or a hyphen removed)
        var edges = textEdges(items);
        for (var i = fromItem; i < items.length; i++) {
            var item = items[i];
            var base = i === fromItem ? fromOffset : 0;
            var text = item.str.slice(base);
            var y = item.transform ? item.transform[5] : 0;
            var height = Math.abs(item.height || 0);
            if ((i === edges.first || i === edges.last) && isPageNumber(text.trim())) {
                text = ""; // the page number in the header or the footer
            } else if (isNoteMark(text, height, blocks.lastHeight)) {
                text = ""; // the number of a footnote, written smaller after a word ("живота си¹⁷"); the end of the line stays
            }
            if (blocks.lastY !== null && text.trim() && Math.abs(y - blocks.lastY) > BLOCK_GAP * Math.max(height, blocks.lastHeight, 1)) {
                blocks.id++;
            }
            if (text.trim()) {
                blocks.lastY = y;
                blocks.lastHeight = height;
            }
            var piece = { node: { page: page, item: i, base: base }, text: text, block: blocks.id };
            if (text) {
                pieces.push(piece);
                last = piece;
            }
            if (text) {
                lineEnded = false;
            }
            if (item.hasEOL && last && !lineEnded) {
                endLine(last, nextText(items, i + 1, edges) || nextPageText);
                lineEnded = true;
            }
        }
        if (last && !lineEnded) {
            endLine(last, nextPageText);
        }
        return pieces;
    }

    function endLine(piece, nextText) {
        if (/[-\u00AD]$/.test(piece.text) && startsWithLowercase(nextText)) {
            piece.text = piece.text.slice(0, -1);
        } else if (!/\s$/.test(piece.text)) {
            piece.text += " ";
        }
    }

    // The first and the last item with text on a page (where the page number is)
    function textEdges(items) {
        var first = -1;
        var last = -1;
        for (var i = 0; i < items.length; i++) {
            if (items[i].str.trim()) {
                if (first < 0) {
                    first = i;
                }
                last = i;
            }
        }
        return { first: first, last: last };
    }

    // The text a page starts with, without its page number
    function pageStart(items) {
        return nextText(items, 0, textEdges(items));
    }

    // A small number (up to 3 digits) right after the text of a line, in letters smaller than the line
    function isNoteMark(text, height, lineHeight) {
        return /^\d{1,3}$/.test(text) && lineHeight > 0 && height > 0 && height < 0.8 * lineHeight;
    }

    // A number alone ("16", "- 16 -", "16/333") is the page number in the header or the footer: it is not read
    function isPageNumber(text) {
        return text.length <= 12 && /^[\d\s.\-–—\/|]+$/.test(text);
    }

    // The text of the next item with text (a page number at the edge of the page does not count)
    function nextText(items, from, edges) {
        for (var i = from; i < items.length; i++) {
            var text = items[i].str.trim();
            if (text && !((i === edges.first || i === edges.last) && isPageNumber(text))) {
                return text;
            }
        }
        return "";
    }

    function start(reader) {
        var pageCount = reader.pageCount;
        var SENTENCE_KEY = reader.positionKey + "-speech"; // the start of the last sentence read: "page:item:offset"
        var shown = null;          // the sentence marked now
        var declaredLanguage = null;
        var player = null;

        // The language the document declares (often missing or wrong; the text decides in the end)
        var metadata = reader.pdf.getMetadata().then(function (data) {
            declaredLanguage = data && data.info && typeof data.info.Language === "string" ? data.info.Language : null;
        }, function () { /* without it the text decides */ });

        function items(page) {
            return reader.textOf(page).then(textItems);
        }

        // The sentences that start on a page, from a place in it (a cursor { page, item, offset }); the next page
        // is read too, so that a sentence that goes on there is read whole. A chunk is { sentences, page, next },
        // where next is the cursor after its last sentence. Pages without text are skipped.
        function chunkFrom(cursor, skipped) {
            if (cursor.page > pageCount) {
                return Promise.resolve(null);
            }
            var pages = [cursor.page];
            if (cursor.page < pageCount) {
                pages.push(cursor.page + 1);
            }
            return Promise.all(pages.map(items)).then(function (all) {
                var blocks = { id: 0, lastY: null, lastHeight: 0 };
                var pieces = pagePieces(cursor.page, all[0], cursor.item, cursor.offset, blocks,
                    all[1] ? pageStart(all[1]) : "");
                if (all[1]) {
                    blocks.lastY = null; // a new page is not a new paragraph
                    pieces = pieces.concat(pagePieces(cursor.page + 1, all[1], 0, 0, blocks, ""));
                }
                var sentences = speech.splitSentences(pieces).filter(function (sentence) {
                    return sentence.startNode.page === cursor.page && !isPageNumber(sentence.text);
                });
                if (!sentences.length) {
                    var following = { page: cursor.page + 1, item: 0, offset: 0 };
                    return skipped < MAX_EMPTY_PAGES ? chunkFrom(following, skipped + 1) : null;
                }
                var last = sentences[sentences.length - 1];
                return {
                    sentences: sentences,
                    page: cursor.page,
                    next: { page: last.endNode.page, item: last.endNode.item, offset: last.endNode.base + last.endOffset }
                };
            });
        }

        // --- the mark (CSS Custom Highlight API, in the spans of the text layer) ---

        function textNodeOf(page, item) {
            var divs = reader.textDivsOf(page);
            var div = divs && divs[item];
            return div && div.firstChild && div.firstChild.nodeType === 3 ? div.firstChild : null;
        }

        function point(page, item, offset) {
            var node = textNodeOf(page, item);
            return node ? { node: node, offset: Math.min(offset, node.length) } : null;
        }

        // The range of the sentence in the drawn pages (a sentence that goes on over a page not drawn yet
        // is marked up to the end of its first page)
        function rangeOf(sentence) {
            var from = point(sentence.startNode.page, sentence.startNode.item, sentence.startNode.base + sentence.startOffset);
            if (!from) {
                return null;
            }
            var to = point(sentence.endNode.page, sentence.endNode.item, sentence.endNode.base + sentence.endOffset);
            if (!to) {
                var divs = reader.textDivsOf(sentence.startNode.page) || [];
                for (var i = divs.length - 1; i >= 0 && !to; i--) {
                    if (divs[i].firstChild) {
                        to = { node: divs[i].firstChild, offset: divs[i].firstChild.length };
                    }
                }
            }
            var range = document.createRange();
            range.setStart(from.node, from.offset);
            range.setEnd(to.node, to.offset);
            return range;
        }

        function canHighlight() {
            return window.CSS && CSS.highlights && typeof window.Highlight === "function";
        }

        function mark(sentence) {
            var range = rangeOf(sentence);
            if (range && canHighlight()) {
                CSS.highlights.set(HIGHLIGHT_NAME, new Highlight(range));
            }
            return range;
        }

        function clear() {
            shown = null;
            if (canHighlight()) {
                CSS.highlights.delete(HIGHLIGHT_NAME);
            }
        }

        // The page follows the reading: a sentence outside the screen is brought to a quarter of its height.
        // A page that is not drawn yet is opened first; the mark comes when it is drawn.
        function follow(sentence, range) {
            var box = stage.getBoundingClientRect();
            if (!range) {
                if (reader.currentPage() !== sentence.startNode.page) {
                    reader.goTo(sentence.startNode.page);
                }
                return;
            }
            var rect = range.getBoundingClientRect();
            if (rect.top < box.top || rect.bottom > box.bottom) {
                stage.scrollTop += rect.top - box.top - box.height * FOLLOW_AT;
            }
        }

        function show(sentence) {
            shown = sentence;
            reader.save(SENTENCE_KEY, [sentence.startNode.page, sentence.startNode.item,
                sentence.startNode.base + sentence.startOffset].join(":"));
            follow(sentence, mark(sentence));
        }

        // A page drawn again (zoom, scrolling back) has new spans: the mark is put on them again
        reader.onDrawn(function (page) {
            if (shown && shown.startNode.page === page) {
                follow(shown, mark(shown));
            } else if (shown && shown.endNode.page === page) {
                mark(shown);
            }
        });

        // --- where to start ---

        function savedCursor() {
            var parts = (reader.load(SENTENCE_KEY, "") || "").split(":").map(function (part) { return parseInt(part, 10); });
            var valid = parts.length === 3 && parts[0] >= 1 && parts.every(function (n) { return n >= 0; });
            return valid ? { page: parts[0], item: parts[1], offset: parts[2] } : null;
        }

        // The first text item on the screen: the end of the page before the current one may still be at the top
        // (the top of the current page when it is not drawn)
        function screenCursor() {
            var current = reader.currentPage();
            var box = stage.getBoundingClientRect();
            for (var page = Math.max(1, current - 1); page <= current; page++) {
                var divs = reader.textDivsOf(page) || [];
                for (var i = 0; i < divs.length; i++) {
                    var rect = divs[i].getBoundingClientRect();
                    if (divs[i].textContent.trim() && rect.bottom > box.top && rect.top < box.bottom) {
                        return { page: page, item: i, offset: 0 };
                    }
                }
            }
            return { page: current, item: 0, offset: 0 };
        }

        // Whether the saved sentence is where the reader is: on the screen, on the current page or on the next
        // one (the saved page is the one at a third of the screen, the sentence read may be lower, on the next page)
        function isNear(cursor) {
            var current = reader.currentPage();
            if (cursor.page === current || cursor.page === current + 1) {
                return true;
            }
            var found = point(cursor.page, cursor.item, cursor.offset);
            if (!found) {
                return false;
            }
            var rect = found.node.parentNode.getBoundingClientRect();
            var box = stage.getBoundingClientRect();
            return rect.bottom > box.top && rect.top < box.bottom;
        }

        // The last sentence read when the reader is still there (the saved page opens there), else the top of
        // the screen
        function startCursor() {
            var saved = savedCursor();
            return saved && saved.page <= pageCount && isNear(saved) ? saved : screenCursor();
        }

        function languageOf(chunk) {
            return speech.guessLanguage(declaredLanguage, chunk.sentences.map(function (s) { return s.text; }).join(" "));
        }

        player = speech.createPlayer("sl-pdf-speech", {
            start: function () {
                return metadata.then(function () {
                    return chunkFrom(startCursor(), 0);
                }).then(function (chunk) {
                    if (!chunk) {
                        return { message: "След тази страница няма текст за четене (сканиран документ?)." };
                    }
                    return { chunk: chunk, index: 0, language: languageOf(chunk) };
                });
            },
            next: function (chunk) {
                return chunkFrom(chunk.next, 0).then(function (next) {
                    if (!next) {
                        reader.save(SENTENCE_KEY, "");
                        return { end: "Краят на документа." };
                    }
                    return next;
                });
            },
            show: show,
            clear: clear,
            language: function () {
                return metadata.then(function () {
                    return chunkFrom(screenCursor(), 0);
                }).then(function (chunk) {
                    return chunk ? languageOf(chunk) : null;
                });
            },
            noVoiceMessage: function (language) {
                return "В този браузър няма глас за езика на документа (" + language + "). " +
                    "Опитайте с Microsoft Edge или инсталирайте глас в настройките на системата.";
            }
        });
        if (!player) {
            return;
        }

        // A page chosen by the reader (the buttons, the page number, the arrows) ends the reading
        stage.addEventListener("sl-pdf-jump", function () {
            if (player.isReading()) {
                player.stop("");
            }
        });
    }
})();
