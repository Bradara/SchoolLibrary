// Read aloud in the EPUB reader. Waits for sl-epub-reader.js to open the book ("sl-epub-ready"); the buttons,
// the voices and the queue are in sl-speech.js. This file gives it the text: the sentences of the chapter
// from the current page on, the mark on the sentence being read, the page that follows it and the next
// chapter at the end of one.
(function () {
    "use strict";

    var BLOCKS = /^(P|H[1-6]|LI|DT|DD|BLOCKQUOTE|PRE|TD|TH|CAPTION|FIGCAPTION|DIV|SECTION|ARTICLE|ASIDE|HEADER|FOOTER|BODY|TR|TABLE|UL|OL|DL)$/;
    var SKIPPED = /^(SCRIPT|STYLE|NOSCRIPT|RT|RP|TEMPLATE)$/;
    var HIGHLIGHT_NAME = "sl-speech";
    var HIGHLIGHT_STYLE_ID = "sl-speech-style";
    var MAX_EMPTY_CHAPTERS = 20; // chapters without text (pictures) skipped one after another

    var area = document.getElementById("sl-epub");
    var speech = window.SlSpeech;
    if (!area || !speech || !speech.supported()) {
        return;
    }
    var splitSentences = speech.splitSentences;

    area.addEventListener("sl-epub-ready", function (event) {
        start(event.detail);
    });

    // The text pieces of a chapter, in reading order
    function textPieces(doc) {
        var pieces = [];
        if (!doc || !doc.body) {
            return pieces;
        }
        var walker = doc.createTreeWalker(doc.body, 4 /* NodeFilter.SHOW_TEXT */);
        var node;
        while ((node = walker.nextNode())) {
            var block = null;
            var skip = false;
            for (var parent = node.parentNode; parent && parent.nodeType === 1; parent = parent.parentNode) {
                var tag = (parent.localName || "").toUpperCase();
                if (SKIPPED.test(tag) || parent.getAttribute("aria-hidden") === "true") {
                    skip = true;
                    break;
                }
                if (!block && BLOCKS.test(tag)) {
                    block = parent;
                }
            }
            if (!skip) {
                pieces.push({ node: node, text: node.nodeValue, block: block || doc.body });
            }
        }
        return pieces;
    }

    function start(reader) {
        var rendition = reader.rendition;
        var h = reader.helpers;
        var book = reader.book;
        var cfiTool = typeof ePub !== "undefined" && ePub.CFI ? new ePub.CFI() : null;
        // The start of the last sentence read (CFI), kept in this browser for each book
        var SENTENCE_KEY = h.positionKey + "-speech";
        var advancingTo = -1;   // the next chapter being opened by the reading (not a jump by the reader)
        var player = null;

        // A chunk is the chapter being read: { sentences, section }

        function contentsOf(sectionIndex) {
            return rendition.getContents().filter(function (c) {
                return c.sectionIndex === sectionIndex;
            })[0] || null;
        }

        // The mark uses the CSS Custom Highlight API: the chapter's DOM is not changed, so the search, the
        // highlights and the positions (CFI) of the reader stay as they are. Without the API nothing is marked.
        function addHighlightStyle(doc) {
            if (!doc || doc.getElementById(HIGHLIGHT_STYLE_ID)) {
                return;
            }
            var style = doc.createElement("style");
            style.id = HIGHLIGHT_STYLE_ID;
            style.textContent = "::highlight(" + HIGHLIGHT_NAME + ") { background-color: rgba(255, 212, 0, 0.4); }";
            (doc.head || doc.documentElement).appendChild(style);
        }

        function highlightsOf(doc) {
            var view = doc && doc.defaultView;
            return view && view.CSS && view.CSS.highlights && typeof view.Highlight === "function" ? view : null;
        }

        function clearHighlight() {
            advancingTo = -1;
            rendition.getContents().forEach(function (c) {
                var view = highlightsOf(c.document);
                if (view) {
                    view.CSS.highlights.delete(HIGHLIGHT_NAME);
                }
            });
        }

        function rangeOf(sentence) {
            var range = sentence.startNode.ownerDocument.createRange();
            range.setStart(sentence.startNode, sentence.startOffset);
            range.setEnd(sentence.endNode, sentence.endOffset);
            return range;
        }

        function isOnPage(cfi, location) {
            return cfiTool.compare(cfi, location.start.cfi) >= 0 && cfiTool.compare(cfi, location.end.cfi) <= 0;
        }

        // Remembers where the sentence starts (to continue from it after the page is opened again) and turns to
        // its page, when no part of it is on the screen. A sentence that starts on the previous page and goes on
        // on this one keeps this page (no turning back and forth).
        function followPage(range, section) {
            var contents = contentsOf(section);
            var location = rendition.currentLocation();
            if (!cfiTool || !contents || !location || !location.start || !location.end) {
                return;
            }
            var start = range.cloneRange();
            start.collapse(true);
            var end = range.cloneRange();
            end.collapse(false);
            var cfi;
            try {
                cfi = contents.cfiFromRange(start);
                h.save(SENTENCE_KEY, cfi);
                var startsAfterPage = cfiTool.compare(cfi, location.end.cfi) > 0;
                var endsBeforePage = cfiTool.compare(contents.cfiFromRange(end), location.start.cfi) < 0;
                if (!startsAfterPage && !endsBeforePage) {
                    return;
                }
            } catch (e) {
                return;
            }
            rendition.display(cfi).catch(function () { /* the reading goes on without turning the page */ });
        }

        // The last sentence read, when it is on the page on the screen (the saved reading position opens that
        // page). Elsewhere the reader has moved on, and the reading starts from the top of the page.
        function savedSentence(place) {
            var cfi = h.load(SENTENCE_KEY, "");
            var location = place.location;
            if (!cfi || !cfiTool || !location || !location.start || !location.end) {
                return null;
            }
            try {
                if (!isOnPage(cfi, location)) {
                    return null;
                }
                var range = rendition.getRange(cfi);
                return range && range.startContainer.ownerDocument === place.contents.document ? range : null;
            } catch (e) {
                return null;
            }
        }

        function showSentence(sentence, chunk) {
            var range;
            try {
                range = rangeOf(sentence);
            } catch (e) {
                return;
            }
            var view = highlightsOf(sentence.startNode.ownerDocument);
            if (view) {
                view.CSS.highlights.set(HIGHLIGHT_NAME, new view.Highlight(range));
            }
            followPage(range, chunk.section);
        }

        // A change of the font or the text settings renders the chapter again: its text nodes are new, so the
        // sentences are split again and the reading goes on from the same sentence
        function refresh(chunk, index) {
            var current = chunk.sentences[index];
            if (current.startNode.isConnected && current.endNode.isConnected) {
                return undefined;
            }
            var contents = contentsOf(chunk.section);
            if (!contents) {
                return null;
            }
            var all = splitSentences(textPieces(contents.document));
            var best = -1;
            for (var i = 0; i < all.length; i++) {
                if (all[i].text === current.text && (best < 0 || Math.abs(i - index) < Math.abs(best - index))) {
                    best = i;
                }
            }
            if (best < 0) {
                return null;
            }
            addHighlightStyle(contents.document);
            return { chunk: { sentences: all, section: chunk.section }, index: best };
        }

        // The end of a chapter: the next chapter of the reading order (chapters without text are skipped)
        function nextChapter(fromSection, skipped) {
            var current = book.spine.get(fromSection);
            var next = current && typeof current.next === "function" ? current.next() : null;
            if (!next) {
                h.save(SENTENCE_KEY, "");
                return Promise.resolve({ end: "Краят на книгата." });
            }
            advancingTo = next.index;
            return rendition.display(next.href).then(function () {
                var contents = contentsOf(next.index);
                var all = contents ? splitSentences(textPieces(contents.document)) : [];
                if (!all.length) {
                    return skipped < MAX_EMPTY_CHAPTERS
                        ? nextChapter(next.index, skipped + 1)
                        : { end: "Няма повече текст за четене." };
                }
                addHighlightStyle(contents.document);
                return { sentences: all, section: next.index };
            }, function () {
                return { end: "Следващата глава не можа да се отвори." };
            });
        }

        // The rendered chapter on the screen and where the page starts in it
        function currentPlace() {
            var location = rendition.currentLocation();
            var startLocation = location && location.start;
            if (!startLocation) {
                return null;
            }
            var contents = rendition.getContents().filter(function (c) {
                return c.sectionIndex === startLocation.index;
            })[0] || rendition.getContents()[0];
            if (!contents || !contents.document) {
                return null;
            }
            var range = null;
            try {
                range = rendition.getRange(startLocation.cfi);
            } catch (e) {
                range = null;
            }
            return { contents: contents, range: range, index: startLocation.index, location: location };
        }

        // All the sentences of the chapter and the one the page starts in
        // (or the last sentence read, when it is on this page)
        function sentencesFromPage(place) {
            var all = splitSentences(textPieces(place.contents.document));
            var from = savedSentence(place) || place.range;
            var first = from ? firstEndingAfter(all, from) : 0;
            return { all: all, first: first, rest: all.slice(first) };
        }

        // The first sentence that ends after the start of the range (the sentence the range starts in)
        function firstEndingAfter(all, range) {
            for (var i = 0; i < all.length; i++) {
                try {
                    if (range.comparePoint(all[i].endNode, all[i].endOffset) > 0) {
                        return i;
                    }
                } catch (e) {
                    return i;
                }
            }
            return all.length;
        }

        // The language of the text on the page (the language a book declares is often wrong)
        function languageOf(place, found) {
            var declared = place.contents.document.documentElement.getAttribute("lang");
            return speech.guessLanguage(declared, found.map(function (sentence) { return sentence.text; }).join(" "));
        }

        player = speech.createPlayer("sl-epub-speech", {
            start: function () {
                var place = currentPlace();
                if (!place) {
                    return { message: "Книгата още се зарежда." };
                }
                var found = sentencesFromPage(place);
                if (!found.rest.length) {
                    return { message: "На тази страница няма текст за четене." };
                }
                addHighlightStyle(place.contents.document);
                return {
                    chunk: { sentences: found.all, section: place.index },
                    index: found.first,
                    language: languageOf(place, found.rest)
                };
            },
            next: function (chunk) {
                return nextChapter(chunk.section, 0);
            },
            show: showSentence,
            clear: clearHighlight,
            refresh: refresh,
            isStale: function (sentence) {
                return !sentence.startNode.isConnected;
            },
            language: function () {
                var place = currentPlace();
                var found = place ? sentencesFromPage(place).rest : [];
                return place && found.length ? languageOf(place, found) : null;
            },
            noVoiceMessage: function (language) {
                return "В този браузър няма глас за езика на книгата (" + language + "). " +
                    "Опитайте с Microsoft Edge или инсталирайте глас в настройките на системата.";
            }
        });
        if (!player) {
            return;
        }

        // Another chapter chosen by the reader (the table of contents, a bookmark, a search result) ends the reading;
        // the next chapter opened by the reading itself does not
        rendition.on("relocated", function (location) {
            var chunk = player.chunk();
            var at = location && location.start ? location.start.index : -1;
            if (chunk && at !== chunk.section && at !== advancingTo) {
                player.stop("");
            }
        });

        rendition.hooks.content.register(function (contents) {
            addHighlightStyle(contents.document);
        });
    }
})();
