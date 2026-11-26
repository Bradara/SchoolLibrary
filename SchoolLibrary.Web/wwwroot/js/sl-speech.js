// Read aloud, shared by the EPUB and the PDF readers (window.SlSpeech). Uses the speech of the browser
// (speechSynthesis): the voices come from the browser and the system, nothing is sent to this site.
// The text is read one sentence at a time (Chrome stops a long text after about 15 seconds); the next
// sentences wait in the browser's queue, so that there is no long pause between them. Pause cancels the
// speech and remembers the sentence, because pause/resume of the browsers is unreliable (Android).
//
// A reader gives a "source" (where the text comes from, how a sentence is shown, what comes next) to
// SlSpeech.createPlayer; the player does the buttons, the voices, the speed and the queue.
(function () {
    "use strict";

    var SENTENCE_SOFT_MAX = 160; // after this length a sentence is cut at a comma, a semicolon or a colon
    var SENTENCE_MAX = 220;      // after this length it is cut at the next space
    var ENDS = ".!?…";
    var CLOSERS = "\"'”’»)]}“";
    var SOFT_ENDS = ",;:";

    // Splits text pieces into sentences. A piece is { node, text, block }: a part of the text, where it comes
    // from (a text node of an EPUB chapter, a text item of a PDF page) and the block it belongs to (a new block
    // always starts a new sentence). Each sentence keeps where it starts and ends in the pieces, so that it
    // can be found again on the page.
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

    var exported = {
        splitSentences: splitSentences,
        voicesFor: voicesFor,
        pickVoice: pickVoice,
        guessLanguage: guessLanguage,
        languagePrefix: languagePrefix
    };
    if (typeof module === "object" && module.exports) {
        module.exports = exported;
    }
    if (typeof window === "undefined") {
        return;
    }

    // The keys come from the EPUB reader, which had read aloud first; the PDF reader shares them, so the
    // chosen voice and speed are the same in both
    var RATE_KEY = "sl-epub-speech-rate";
    var VOICES_KEY = "sl-epub-speech-voices"; // the chosen voice per language: { "bg": "<voice id>" }
    var RATES = ["0.75", "1", "1.25", "1.5", "2"];
    var VOICES_WAIT_MS = 2000;
    var QUEUE_AHEAD = 2;         // sentences given to the browser ahead of the one being read

    function load(key, fallback) {
        try {
            var value = localStorage.getItem(key);
            return value === null ? fallback : value;
        } catch (e) {
            return fallback;
        }
    }

    function save(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch (e) {
            // reading works without the saved value
        }
    }

    function supported() {
        return "speechSynthesis" in window && typeof window.SpeechSynthesisUtterance !== "undefined";
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

    function languageName(lang) {
        try {
            return new Intl.DisplayNames(["bg"], { type: "language" }).of(languagePrefix(lang)) || lang;
        } catch (e) {
            return lang;
        }
    }

    // The buttons are found by a common prefix ("sl-epub-speech" → sl-epub-speech-play, -stop, -rate, ...).
    // The source is an object with:
    //   start()            → { chunk, index, language } or { message } (or a promise of it): what to read
    //                        from the place on the screen. A chunk is { sentences: [...] } plus anything the
    //                        source needs to find what comes after it.
    //   next(chunk)        → the chunk after this one, or { end: message } (or a promise of it)
    //   show(sentence, chunk)  marks the sentence and brings it on the screen
    //   clear()            removes the mark
    //   language()         → the language of the text on the screen, for the voice settings (or null)
    //   refresh(chunk, index) (optional) → { chunk, index } when the text was rendered again, null when the
    //                        sentence is gone, nothing when all is as it was
    //   isStale(sentence)  (optional) → true when the sentence is no longer on the page (rendered again)
    //   noVoiceMessage     (optional) the message without a voice for the language
    function createPlayer(prefix, source) {
        var synth = window.speechSynthesis;
        function el(name) {
            return document.getElementById(prefix + name);
        }

        var group = el("");
        var playButton = el("-play");
        var stopButton = el("-stop");
        var rateSelect = el("-rate");
        var voiceSelect = el("-voice");
        var voiceHint = el("-voice-hint");
        var settingsButton = el("-settings-btn");
        var status = el("-status");
        if (!group || !playButton || !stopButton || !rateSelect || !voiceSelect) {
            return null;
        }
        group.classList.remove("d-none");

        var storedRate = load(RATE_KEY, "1");
        rateSelect.value = RATES.indexOf(storedRate) >= 0 ? storedRate : "1";

        var chunk = null;       // the part being read (null = nothing started)
        var index = 0;          // the sentence being read, or the one to continue from after a pause
        var language = "bg";
        var voice = null;
        var playing = false;
        var generation = 0;     // a cancelled utterance may still report "end": only the current one counts
        var utterances = [];    // kept, so that the browser does not drop them before they end
        var queuedUpTo = 0;     // the next sentence to put in the browser's queue
        var voiceList = [];     // the voices offered in the settings for their language

        function chosenVoices() {
            try {
                var value = JSON.parse(load(VOICES_KEY, "{}"));
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
            save(VOICES_KEY, JSON.stringify(all));
        }

        function setStatus(text) {
            if (!status) {
                return;
            }
            status.textContent = text || "";
            status.classList.toggle("d-none", !text);
        }

        function render() {
            var paused = !playing && chunk !== null;
            var icon = playButton.querySelector("i");
            var label = playButton.querySelector("span");
            icon.className = "bi " + (playing ? "bi-pause-fill" : paused ? "bi-play-fill" : "bi-volume-up");
            label.textContent = playing ? "Пауза" : paused ? "Продължи" : "Чети на глас";
            playButton.setAttribute("aria-pressed", playing ? "true" : "false");
            stopButton.disabled = chunk === null;
        }

        function rate() {
            var value = parseFloat(rateSelect.value);
            return isFinite(value) && value > 0 ? value : 1;
        }

        function silence() {
            generation++;
            utterances = [];
            synth.cancel();
        }

        function stop(message) {
            silence();
            source.clear();
            playing = false;
            chunk = null;
            index = 0;
            setStatus(message || "");
            render();
        }

        // The end of a chunk: the source finds the next one (the next chapter, the next page)
        function advance() {
            var mine = ++generation;
            var from = chunk;
            Promise.resolve(source.next(from)).then(function (result) {
                if (mine !== generation || chunk !== from) {
                    return; // paused or stopped meanwhile: "Продължи" asks again from the same chunk
                }
                if (!result || result.end !== undefined || !result.sentences || !result.sentences.length) {
                    stop(result && result.end ? result.end : "");
                    return;
                }
                chunk = result;
                index = 0;
                speakCurrent();
            }, function () {
                if (mine === generation) {
                    stop("Четенето не можа да продължи.");
                }
            });
        }

        function speakCurrent() {
            if (!chunk) {
                return;
            }
            if (index >= chunk.sentences.length) {
                advance();
                return;
            }
            if (source.refresh) {
                var fresh = source.refresh(chunk, index);
                if (fresh === null) {
                    stop("Текстът на страницата се промени. Натиснете „Чети на глас“ отново.");
                    return;
                }
                if (fresh) {
                    chunk = fresh.chunk;
                    index = fresh.index;
                }
            }
            source.show(chunk.sentences[index], chunk);
            var mine = ++generation;
            queuedUpTo = index;
            fillQueue(mine);
        }

        // The next sentences wait in the browser's own queue, so that each one starts as soon as the one before
        // ends (one at a time, the voice starts anew after every sentence and the pauses are long, more so with
        // the online voices). The mark moves when a sentence really starts.
        function fillQueue(mine) {
            while (queuedUpTo < chunk.sentences.length && queuedUpTo <= index + QUEUE_AHEAD) {
                enqueue(queuedUpTo, mine);
                queuedUpTo++;
            }
        }

        function enqueue(i, mine) {
            var mineChunk = chunk;
            var u = new window.SpeechSynthesisUtterance(mineChunk.sentences[i].text);
            u.lang = voice.lang || language;
            u.voice = voice;
            u.rate = rate();
            u.onstart = function () {
                if (mine !== generation) {
                    return;
                }
                index = i;
                var sentence = mineChunk.sentences[i];
                if (source.isStale && source.isStale(sentence)) {
                    // the text was rendered again (font, settings): start anew from this sentence
                    silence();
                    speakCurrent();
                    return;
                }
                source.show(sentence, mineChunk);
            };
            u.onend = function () {
                if (mine !== generation) {
                    return;
                }
                index = i + 1;
                if (index >= mineChunk.sentences.length) {
                    advance();
                    return;
                }
                fillQueue(mine);
            };
            u.onerror = function (e) {
                if (mine !== generation || e.error === "interrupted" || e.error === "canceled") {
                    return;
                }
                stop(e.error === "not-allowed"
                    ? "Браузърът не позволи четенето. Натиснете „Чети на глас“ отново."
                    : "Четенето спря поради грешка в браузъра.");
            };
            utterances.push(u);
            if (utterances.length > QUEUE_AHEAD + 4) {
                utterances.shift();
            }
            synth.speak(u);
        }

        function begin() {
            playButton.disabled = true;
            Promise.resolve(source.start()).then(function (found) {
                if (!found || found.message || !found.chunk || !found.chunk.sentences.length) {
                    playButton.disabled = false;
                    setStatus(found && found.message ? found.message : "На тази страница няма текст за четене.");
                    return null;
                }
                language = found.language || "bg";
                return loadVoices().then(function (voices) {
                    playButton.disabled = false;
                    voice = pickVoice(voices, language, chosenVoiceId(language));
                    if (!voice) {
                        setStatus(source.noVoiceMessage
                            ? source.noVoiceMessage(language)
                            : "В този браузър няма глас за езика на текста (" + language + "). " +
                              "Опитайте с Microsoft Edge или инсталирайте глас в настройките на системата.");
                        return;
                    }
                    silence();
                    chunk = found.chunk;
                    index = Math.max(0, found.index || 0);
                    playing = true;
                    setStatus("");
                    render();
                    speakCurrent();
                });
            }).catch(function () {
                playButton.disabled = false;
                setStatus("Текстът не можа да се прочете.");
            });
        }

        playButton.addEventListener("click", function () {
            if (playing) {
                silence();
                playing = false;
                render();
            } else if (chunk !== null) {
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
            save(RATE_KEY, rateSelect.value);
            if (playing) {
                // the new speed takes effect from the start of the current sentence
                silence();
                speakCurrent();
            }
        });

        // The voices for the language of the text; filled each time the settings open (the page may have changed)
        function fillVoices() {
            Promise.resolve(chunk === null ? source.language() : language).then(function (found) {
                var lang = found || language;
                return loadVoices().then(function (voices) {
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
            });
        }

        if (settingsButton) {
            settingsButton.addEventListener("show.bs.dropdown", fillVoices);
        }

        voiceSelect.addEventListener("change", function () {
            var lang = voiceSelect.getAttribute("data-language") || language;
            saveChosenVoice(lang, voiceSelect.value);
            if (chunk === null || languagePrefix(lang) !== languagePrefix(language)) {
                return;
            }
            voice = pickVoice(voiceList, language, voiceSelect.value) || voice;
            if (playing) {
                // the new voice takes effect from the start of the current sentence
                silence();
                speakCurrent();
            }
        });

        // Some browsers go on speaking after the page is left
        window.addEventListener("pagehide", function () {
            synth.cancel();
        });

        synth.cancel(); // speech left over from another page of this site
        render();

        return {
            stop: stop,
            // the part being read (null = nothing started)
            chunk: function () { return chunk; },
            isReading: function () { return chunk !== null; }
        };
    }

    exported.supported = supported;
    exported.loadVoices = loadVoices;
    exported.load = load;
    exported.save = save;
    exported.createPlayer = createPlayer;
    window.SlSpeech = exported;
})();
