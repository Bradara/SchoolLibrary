// Read aloud in the EPUB reader. Waits for sl-epub-reader.js to open the book ("sl-epub-ready").
// Uses the speech of the browser (speechSynthesis): the voices come from the browser and the system,
// nothing is sent to this site. Reads from the current page on, one sentence at a time (Chrome stops a long
// text after about 15 seconds), marks the sentence, turns the pages and goes on with the next chapter.
// Pause cancels the speech and remembers the sentence, because pause/resume of the browsers is unreliable (Android).
(function () {
    "use strict";

    var SENTENCE_SOFT_MAX = 160; // after this length a sentence is cut at a comma, a semicolon or a colon
    var SENTENCE_MAX = 220;      // after this length it is cut at the next space
    var BLOCKS = /^(P|H[1-6]|LI|DT|DD|BLOCKQUOTE|PRE|TD|TH|CAPTION|FIGCAPTION|DIV|SECTION|ARTICLE|ASIDE|HEADER|FOOTER|BODY|TR|TABLE|UL|OL|DL)$/;
    var SKIPPED = /^(SCRIPT|STYLE|NOSCRIPT|RT|RP|TEMPLATE)$/;
    var ENDS = ".!?…";
    var CLOSERS = "\"'”’»)]}“";
    var SOFT_ENDS = ",;:";

    // Splits text pieces into sentences. A piece is { node, text, block }: a text node of the chapter, its text
    // and the block it belongs to (a new block always starts a new sentence). Each sentence keeps where it
    // starts and ends in the text nodes, so that it can be found again on the page.
    function splitSentences(pieces) {
        var result = [];
        var current = null;

        function flush() {
            if (current) {
                var text = current.text.replace(/[­​‌‍﻿]/g, "").replace(/\s+/g, " ").trim();
                if (/[\p{L}\p{N}]/u.test(text)) {
                    result.push({
                        text: text,
                        startNode: current.startNode,
                        startOffset: current.startOffset,
                        endNode: current.endNode,
                        endOffset: current.endOffset
                    });
                }
            }
            current = null;
        }

        pieces.forEach(function (piece) {
            var s = piece.text || "";
            if (current && current.block !== piece.block) {
                flush();
            }
            for (var i = 0; i < s.length; i++) {
                var ch = s.charAt(i);
                var space = /\s/.test(ch);
                if (!current) {
                    if (space) {
                        continue;
                    }
                    current = { text: "", block: piece.block, startNode: piece.node, startOffset: i };
                }
                current.text += ch;
                current.endNode = piece.node;
                current.endOffset = i + 1;

                if (ENDS.indexOf(ch) >= 0) {
                    // take the closing quotes and brackets (and "?!", "...") with the sentence
                    while (i + 1 < s.length && (ENDS + CLOSERS).indexOf(s.charAt(i + 1)) >= 0) {
                        i++;
                        current.text += s.charAt(i);
                        current.endOffset = i + 1;
                    }
                    if (i + 1 >= s.length || /\s/.test(s.charAt(i + 1))) {
                        flush();
                    }
                } else if (space && current.text.length >= SENTENCE_MAX) {
                    flush();
                } else if (space && current.text.length >= SENTENCE_SOFT_MAX &&
                    SOFT_ENDS.indexOf(current.text.charAt(current.text.length - 2)) >= 0) {
                    flush();
                }
            }
        });
        flush();
        return result;
    }

    function normalizeLanguage(language) {
        return (language || "").trim().toLowerCase().replace(/_/g, "-");
    }

    function languagePrefix(language) {
        return normalizeLanguage(language).split("-")[0] || "bg";
    }

    // The id of a voice that stays the same between visits
    function voiceId(voice) {
        return voice.voiceURI || voice.name || "";
    }

    // The voices for a language ("bg" or "bg-BG"): the exact language first, then by name
    function voicesFor(voices, language) {
        var wanted = normalizeLanguage(language) || "bg";
        var prefix = languagePrefix(wanted);
        return (voices || []).filter(function (voice) {
            var lang = normalizeLanguage(voice.lang);
            return lang === prefix || lang.indexOf(prefix + "-") === 0;
        }).sort(function (a, b) {
            var exactA = normalizeLanguage(a.lang) === wanted ? 0 : 1;
            var exactB = normalizeLanguage(b.lang) === wanted ? 0 : 1;
            return exactA - exactB || String(a.name).localeCompare(String(b.name));
        });
    }

    // The voice for a language: the chosen one (if it is still there), else the exact language first and the
    // system default among them. Without a voice for the language the text is not read (a Bulgarian book read
    // by an English voice is unusable).
    function pickVoice(voices, language, preferredId) {
        var matching = voicesFor(voices, language);
        if (!matching.length) {
            return null;
        }
        if (preferredId) {
            var chosen = matching.filter(function (voice) { return voiceId(voice) === preferredId; })[0];
            if (chosen) {
                return chosen;
            }
        }
        var wanted = normalizeLanguage(language) || "bg";
        var exact = matching.filter(function (voice) { return normalizeLanguage(voice.lang) === wanted; });
        var pool = exact.length ? exact : matching;
        return pool.filter(function (voice) { return voice.default; })[0] || pool[0];
    }

    // Books often declare a wrong language (a Bulgarian book marked "en"). The script of the text decides:
    // Cyrillic text in a language written in Latin is read as Bulgarian, Latin text in a Cyrillic language as English.
    var CYRILLIC_LANGUAGES = ["bg", "ru", "uk", "sr", "mk", "be", "kk", "ky", "mn", "tg"];

    function guessLanguage(declared, text) {
        var language = (declared || "").trim() || "bg";
        var sample = (text || "").slice(0, 4000);
        var cyrillic = (sample.match(/\p{Script=Cyrillic}/gu) || []).length;
        var latin = (sample.match(/\p{Script=Latin}/gu) || []).length;
        if (cyrillic + latin < 20) {
            return language;
        }
        var isCyrillicLanguage = CYRILLIC_LANGUAGES.indexOf(languagePrefix(language)) >= 0;
        if (cyrillic > latin * 2 && !isCyrillicLanguage) {
            return "bg";
        }
        if (latin > cyrillic * 2 && isCyrillicLanguage) {
            return "en";
        }
        return language;
    }

    if (typeof module === "object" && module.exports) {
        module.exports = {
            splitSentences: splitSentences,
            voicesFor: voicesFor,
            pickVoice: pickVoice,
            guessLanguage: guessLanguage
        };
    }
    if (typeof document === "undefined") {
        return;
    }

    var area = document.getElementById("sl-epub");
    if (!area || !("speechSynthesis" in window) || typeof window.SpeechSynthesisUtterance === "undefined") {
        return;
    }

    var RATE_KEY = "sl-epub-speech-rate";
    var VOICES_KEY = "sl-epub-speech-voices"; // the chosen voice per language: { "bg": "<voice id>" }
    var RATES = ["0.75", "1", "1.25", "1.5", "2"];
    var VOICES_WAIT_MS = 2000;
    var HIGHLIGHT_NAME = "sl-speech";
    var HIGHLIGHT_STYLE_ID = "sl-speech-style";
    var MAX_EMPTY_CHAPTERS = 20; // chapters without text (pictures) skipped one after another

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

    // Voices load asynchronously in Chrome and Edge
    function loadVoices() {
        var synth = window.speechSynthesis;
        var now = synth.getVoices();
        if (now && now.length) {
            return Promise.resolve(now);
        }
        return new Promise(function (resolve) {
            var done = false;
            function finish() {
                if (!done) {
                    done = true;
                    synth.removeEventListener("voiceschanged", finish);
                    resolve(synth.getVoices() || []);
                }
            }
            synth.addEventListener("voiceschanged", finish);
            window.setTimeout(finish, VOICES_WAIT_MS);
        });
    }

    function start(reader) {
        var rendition = reader.rendition;
        var h = reader.helpers;
        var el = h.el;
        var synth = window.speechSynthesis;

        var group = el("sl-epub-speech");
        var playButton = el("sl-epub-speech-play");
        var stopButton = el("sl-epub-speech-stop");
        var rateSelect = el("sl-epub-speech-rate");
        var voiceSelect = el("sl-epub-speech-voice");
        var voiceHint = el("sl-epub-speech-voice-hint");
        var settingsButton = el("sl-epub-speech-settings-btn");
        var status = el("sl-epub-speech-status");
        if (!group || !playButton || !stopButton || !rateSelect || !voiceSelect) {
            return;
        }
        group.classList.remove("d-none");

        var storedRate = h.load(RATE_KEY, "1");
        rateSelect.value = RATES.indexOf(storedRate) >= 0 ? storedRate : "1";

        var sentences = null;   // the sentences being read (null = nothing started)
        var index = 0;          // the sentence being read, or the one to continue from after a pause
        var section = -1;       // the chapter being read
        var language = "bg";
        var voice = null;
        var playing = false;
        var generation = 0;     // a cancelled utterance may still report "end": only the current one counts
        var utterance = null;   // kept, so that the browser does not drop it before it ends
        var voiceList = [];     // the voices offered in the settings for their language
        var advancingTo = -1;   // the next chapter being opened by the reading (not a jump by the reader)
        var book = reader.book;
        var cfiTool = typeof ePub !== "undefined" && ePub.CFI ? new ePub.CFI() : null;

        function chosenVoices() {
            try {
                var value = JSON.parse(h.load(VOICES_KEY, "{}"));
                return value && typeof value === "object" ? value : {};
            } catch (e) {
                return {};
            }
        }

        function chosenVoiceId(lang) {
            var id = chosenVoices()[languagePrefix(lang)];
            return typeof id === "string" ? id : "";
        }

        function saveChosenVoice(lang, id) {
            var all = chosenVoices();
            if (id) {
                all[languagePrefix(lang)] = id;
            } else {
                delete all[languagePrefix(lang)];
            }
            h.save(VOICES_KEY, JSON.stringify(all));
        }

        function languageName(lang) {
            try {
                return new Intl.DisplayNames(["bg"], { type: "language" }).of(languagePrefix(lang)) || lang;
            } catch (e) {
                return lang;
            }
        }

        function setStatus(text) {
            if (!status) {
                return;
            }
            status.textContent = text || "";
            status.classList.toggle("d-none", !text);
        }

        function render() {
            var paused = !playing && sentences !== null;
            var icon = playButton.querySelector("i");
            var label = playButton.querySelector("span");
            icon.className = "bi " + (playing ? "bi-pause-fill" : paused ? "bi-play-fill" : "bi-volume-up");
            label.textContent = playing ? "Пауза" : paused ? "Продължи" : "Чети на глас";
            playButton.setAttribute("aria-pressed", playing ? "true" : "false");
            stopButton.disabled = sentences === null;
        }

        function rate() {
            var value = parseFloat(rateSelect.value);
            return isFinite(value) && value > 0 ? value : 1;
        }

        function silence() {
            generation++;
            utterance = null;
            synth.cancel();
        }

        function stop(message) {
            silence();
            clearHighlight();
            playing = false;
            sentences = null;
            index = 0;
            section = -1;
            advancingTo = -1;
            setStatus(message || "");
            render();
        }

        // --- the sentence being read: marked in the book, and the page follows it ---

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

        // Turns to the page where the sentence starts, when it is not on the screen
        function followPage(range) {
            var contents = contentsOf(section);
            var location = rendition.currentLocation();
            if (!cfiTool || !contents || !location || !location.start || !location.end) {
                return;
            }
            var start = range.cloneRange();
            start.collapse(true);
            var cfi;
            try {
                cfi = contents.cfiFromRange(start);
                if (cfiTool.compare(cfi, location.start.cfi) >= 0 && cfiTool.compare(cfi, location.end.cfi) <= 0) {
                    return;
                }
            } catch (e) {
                return;
            }
            rendition.display(cfi).catch(function () { /* the reading goes on without turning the page */ });
        }

        function showSentence(sentence) {
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
            followPage(range);
        }

        // A change of the font or the text settings renders the chapter again: its text nodes are new, so the
        // sentences are split again and the reading goes on from the same sentence
        function refreshSentences() {
            var current = sentences[index];
            if (current.startNode.isConnected && current.endNode.isConnected) {
                return true;
            }
            var contents = contentsOf(section);
            if (!contents) {
                return false;
            }
            var all = splitSentences(textPieces(contents.document));
            var best = -1;
            for (var i = 0; i < all.length; i++) {
                if (all[i].text === current.text && (best < 0 || Math.abs(i - index) < Math.abs(best - index))) {
                    best = i;
                }
            }
            if (best < 0) {
                return false;
            }
            sentences = all;
            index = best;
            addHighlightStyle(contents.document);
            return true;
        }

        // The end of a chapter: the next chapter of the reading order (chapters without text are skipped)
        function nextChapter(skipped) {
            var current = book.spine.get(section);
            var next = current && typeof current.next === "function" ? current.next() : null;
            if (!next) {
                stop("Краят на книгата.");
                return;
            }
            var mine = ++generation;
            advancingTo = next.index;
            rendition.display(next.href).then(function () {
                if (mine !== generation) {
                    return; // paused or stopped meanwhile: "Продължи" goes on from here
                }
                advancingTo = -1;
                section = next.index;
                var contents = contentsOf(section);
                var all = contents ? splitSentences(textPieces(contents.document)) : [];
                if (!all.length) {
                    if (skipped < MAX_EMPTY_CHAPTERS) {
                        nextChapter(skipped + 1);
                    } else {
                        stop("Няма повече текст за четене.");
                    }
                    return;
                }
                addHighlightStyle(contents.document);
                sentences = all;
                index = 0;
                speakCurrent();
            }).catch(function () {
                if (mine === generation) {
                    stop("Следващата глава не можа да се отвори.");
                }
            });
        }

        function speakCurrent() {
            if (!sentences) {
                return;
            }
            if (index >= sentences.length) {
                nextChapter(0);
                return;
            }
            if (!refreshSentences()) {
                stop("Текстът на страницата се промени. Натиснете „Чети на глас“ отново.");
                return;
            }
            showSentence(sentences[index]);
            var mine = ++generation;
            var u = new window.SpeechSynthesisUtterance(sentences[index].text);
            u.lang = voice.lang || language;
            u.voice = voice;
            u.rate = rate();
            u.onend = function () {
                if (mine !== generation) {
                    return;
                }
                index++;
                speakCurrent();
            };
            u.onerror = function (e) {
                if (mine !== generation || e.error === "interrupted" || e.error === "canceled") {
                    return;
                }
                stop(e.error === "not-allowed"
                    ? "Браузърът не позволи четенето. Натиснете „Чети на глас“ отново."
                    : "Четенето спря поради грешка в браузъра.");
            };
            utterance = u;
            synth.speak(u);
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
            return { contents: contents, range: range, index: startLocation.index };
        }

        // All the sentences of the chapter and the one the page starts in
        function sentencesFromPage(place) {
            var all = splitSentences(textPieces(place.contents.document));
            if (!place.range) {
                return { all: all, first: 0, rest: all };
            }
            var first = 0;
            for (var i = 0; i < all.length; i++) {
                var after;
                try {
                    after = place.range.comparePoint(all[i].endNode, all[i].endOffset) > 0;
                } catch (e) {
                    after = true;
                }
                if (after) {
                    first = i;
                    break;
                }
                first = i + 1;
            }
            return { all: all, first: first, rest: all.slice(first) };
        }

        // The language of the text on the page (the language a book declares is often wrong)
        function languageOf(place, found) {
            var declared = place.contents.document.documentElement.getAttribute("lang");
            return guessLanguage(declared, found.map(function (sentence) { return sentence.text; }).join(" "));
        }

        function begin() {
            var place = currentPlace();
            if (!place) {
                setStatus("Книгата още се зарежда.");
                return;
            }
            var found = sentencesFromPage(place);
            if (!found.rest.length) {
                setStatus("На тази страница няма текст за четене.");
                return;
            }
            language = languageOf(place, found.rest);
            playButton.disabled = true;
            loadVoices().then(function (voices) {
                playButton.disabled = false;
                voice = pickVoice(voices, language, chosenVoiceId(language));
                if (!voice) {
                    setStatus("В този браузър няма глас за езика на книгата (" + language + "). " +
                        "Опитайте с Microsoft Edge или инсталирайте глас в настройките на системата.");
                    return;
                }
                silence();
                addHighlightStyle(place.contents.document);
                sentences = found.all;
                index = found.first;
                section = place.index;
                playing = true;
                setStatus("");
                render();
                speakCurrent();
            });
        }

        playButton.addEventListener("click", function () {
            if (playing) {
                silence();
                playing = false;
                render();
            } else if (sentences !== null) {
                playing = true;
                render();
                speakCurrent();
            } else {
                begin();
            }
        });

        stopButton.addEventListener("click", function () {
            stop("");
        });

        rateSelect.addEventListener("change", function () {
            h.save(RATE_KEY, rateSelect.value);
            if (playing) {
                // the new speed takes effect from the start of the current sentence
                silence();
                speakCurrent();
            }
        });

        // The voices for the language of the page; filled each time the settings open (the page may have changed)
        function fillVoices() {
            var lang = language;
            if (sentences === null) {
                var place = currentPlace();
                var found = place ? sentencesFromPage(place).rest : [];
                if (place && found.length) {
                    lang = languageOf(place, found);
                }
            }
            loadVoices().then(function (voices) {
                voiceList = voicesFor(voices, lang);
                var chosen = chosenVoiceId(lang);
                while (voiceSelect.options.length > 1) {
                    voiceSelect.remove(1);
                }
                voiceList.forEach(function (v) {
                    var option = document.createElement("option");
                    option.value = voiceId(v);
                    option.textContent = v.name + (v.localService === false ? " (онлайн)" : "");
                    voiceSelect.appendChild(option);
                });
                voiceSelect.value = voiceList.some(function (v) { return voiceId(v) === chosen; }) ? chosen : "";
                voiceSelect.disabled = !voiceList.length;
                voiceSelect.setAttribute("data-language", lang);
                if (voiceHint) {
                    voiceHint.textContent = voiceList.length
                        ? "Език на текста: " + languageName(lang) + "."
                        : "В този браузър няма глас за езика на текста (" + languageName(lang) + ").";
                }
            });
        }

        if (settingsButton) {
            settingsButton.addEventListener("show.bs.dropdown", fillVoices);
        }

        voiceSelect.addEventListener("change", function () {
            var lang = voiceSelect.getAttribute("data-language") || language;
            saveChosenVoice(lang, voiceSelect.value);
            if (sentences === null || languagePrefix(lang) !== languagePrefix(language)) {
                return;
            }
            voice = pickVoice(voiceList, language, voiceSelect.value) || voice;
            if (playing) {
                // the new voice takes effect from the start of the current sentence
                silence();
                speakCurrent();
            }
        });

        // Another chapter chosen by the reader (the table of contents, a bookmark, a search result) ends the reading;
        // the next chapter opened by the reading itself does not
        rendition.on("relocated", function (location) {
            var at = location && location.start ? location.start.index : -1;
            if (sentences !== null && at !== section && at !== advancingTo) {
                stop("");
            }
        });

        rendition.hooks.content.register(function (contents) {
            addHighlightStyle(contents.document);
        });

        // Some browsers go on speaking after the page is left
        window.addEventListener("pagehide", function () {
            synth.cancel();
        });

        synth.cancel(); // speech left over from another page of this site
        render();
    }
})();
