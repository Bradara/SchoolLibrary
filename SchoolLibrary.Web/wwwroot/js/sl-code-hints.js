// Completion hints for the code editor (sl-code-editor.js): keywords, common functions and the names already
// used in the program. Not real IntelliSense — it does not know the types; it only saves typing.
// Ctrl+Space opens the list; it also opens by itself after a letter or a dot (not inside strings and comments).
// A name ending in "(" is a function: it is inserted with "()" and the cursor goes between the brackets.
(function () {
    "use strict";

    var languages = {
        csharp: {
            keywords: [
                "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char",
                "checked", "class", "const", "continue", "decimal", "default", "do", "double", "else", "enum",
                "false", "finally", "float", "for", "foreach", "if", "in", "int", "interface", "internal", "is",
                "long", "namespace", "new", "null", "object", "out", "override", "params", "private", "protected",
                "public", "readonly", "record", "ref", "return", "sbyte", "short", "static", "string", "struct",
                "switch", "this", "throw", "true", "try", "uint", "ulong", "using", "var", "virtual", "void",
                "while", "yield", "nameof", "typeof"
            ],
            globals: [
                "Console.WriteLine(", "Console.Write(", "Console.ReadLine(", "Console.ReadKey(", "Console.Read(",
                "int.Parse(", "int.TryParse(", "int.MaxValue", "int.MinValue",
                "long.Parse(", "long.MaxValue", "long.MinValue", "double.Parse(", "double.TryParse(",
                "decimal.Parse(", "char.IsDigit(", "char.IsLetter(", "char.IsLetterOrDigit(", "char.IsUpper(",
                "char.IsLower(", "char.IsWhiteSpace(", "char.ToUpper(", "char.ToLower(",
                "string.Join(", "string.Concat(", "string.Format(", "string.IsNullOrEmpty(",
                "string.IsNullOrWhiteSpace(", "string.Empty",
                "Math.Abs(", "Math.Max(", "Math.Min(", "Math.Pow(", "Math.Sqrt(", "Math.Round(", "Math.Floor(",
                "Math.Ceiling(", "Math.Truncate(", "Math.Sign(", "Math.PI",
                "Convert.ToInt32(", "Convert.ToInt64(", "Convert.ToDouble(", "Convert.ToDecimal(",
                "Convert.ToString(", "Convert.ToChar(",
                "Array.Sort(", "Array.Reverse(", "Array.IndexOf(", "Array.Fill(", "Array.Empty",
                "Enumerable.Range(", "Enumerable.Repeat(",
                "StringSplitOptions.RemoveEmptyEntries", "Environment.NewLine",
                "List<int>", "List<string>", "List<double>", "Dictionary<string, int>", "Dictionary<int, int>",
                "HashSet<int>", "HashSet<string>", "Queue<int>", "Stack<int>", "StringBuilder",
                "System", "System.Linq", "System.Text", "System.Collections.Generic"
            ],
            members: [
                "Length", "Count", "Keys", "Values",
                "Add(", "AddRange(", "Insert(", "Remove(", "RemoveAt(", "Clear(", "Contains(", "ContainsKey(",
                "TryGetValue(", "IndexOf(", "LastIndexOf(", "Sort(", "Reverse(", "ToArray(", "ToList(",
                "Split(", "Trim(", "TrimStart(", "TrimEnd(", "ToUpper(", "ToLower(", "Substring(", "Replace(",
                "StartsWith(", "EndsWith(", "PadLeft(", "PadRight(", "ToCharArray(", "ToString(", "Equals(",
                "CompareTo(", "Push(", "Pop(", "Peek(", "Enqueue(", "Dequeue(", "Append(", "AppendLine(",
                "Select(", "Where(", "Sum(", "Max(", "Min(", "Average(", "OrderBy(", "OrderByDescending(",
                "ThenBy(", "First(", "FirstOrDefault(", "Last(", "Any(", "All(", "Distinct(", "Skip(", "Take("
            ]
        },
        python3: {
            keywords: [
                "False", "None", "True", "and", "as", "assert", "break", "class", "continue", "def", "del",
                "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is",
                "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield"
            ],
            globals: [
                "print(", "input(", "int(", "float(", "str(", "bool(", "len(", "range(", "list(", "dict(",
                "set(", "tuple(", "sorted(", "reversed(", "sum(", "min(", "max(", "abs(", "round(",
                "enumerate(", "zip(", "map(", "filter(", "any(", "all(", "ord(", "chr(", "isinstance(",
                "divmod(", "pow(", "math", "math.sqrt(", "math.floor(", "math.ceil(", "math.gcd(",
                "math.factorial(", "math.pi", "sys", "sys.stdin.readline(", "sys.stdin.read("
            ],
            members: [
                "split(", "strip(", "lstrip(", "rstrip(", "join(", "replace(", "upper(", "lower(", "title(",
                "isdigit(", "isalpha(", "isalnum(", "isupper(", "islower(", "startswith(", "endswith(",
                "find(", "count(", "format(", "zfill(", "append(", "extend(", "insert(", "pop(", "remove(",
                "sort(", "reverse(", "index(", "copy(", "clear(", "keys(", "values(", "items(", "get(",
                "add(", "discard(", "update("
            ]
        }
    };

    var wordPattern = /[A-Za-z_Ѐ-ӿ][\wЀ-ӿ]*/g;

    function startsWith(text, prefix) {
        return text.toLowerCase().indexOf(prefix.toLowerCase()) === 0;
    }

    // Exact-case matches first, then the rest in their list order
    function rank(list, prefix) {
        var exact = [];
        var other = [];
        list.forEach(function (item) {
            if (item.indexOf(prefix) === 0) {
                exact.push(item);
            } else {
                other.push(item);
            }
        });
        return exact.concat(other);
    }

    function unique(list) {
        var seen = {};
        return list.filter(function (item) {
            if (seen[item]) {
                return false;
            }
            seen[item] = true;
            return true;
        });
    }

    // The names typed in the program, except the one being typed now
    function programWords(cm, current) {
        var words = [];
        var cursor = cm.getCursor();
        cm.eachLine(function (handle) {
            var line = cm.getLineNumber(handle);
            var text = handle.text;
            var match;
            wordPattern.lastIndex = 0;
            while ((match = wordPattern.exec(text)) !== null) {
                var isCurrent = line === cursor.line
                    && match.index <= cursor.ch && match.index + match[0].length >= cursor.ch;
                if (match[0].length > 2 && !isCurrent && match[0] !== current) {
                    words.push(match[0]);
                }
            }
        });
        return words;
    }

    function completion(item) {
        var isFunction = item.charAt(item.length - 1) === "(";
        return {
            text: isFunction ? item + ")" : item,
            displayText: isFunction ? item + ")" : item,
            hint: function (cm, data, self) {
                cm.replaceRange(self.text, data.from, data.to, "complete");
                if (isFunction) {
                    var at = cm.getCursor();
                    cm.setCursor({ line: at.line, ch: at.ch - 1 });
                }
            }
        };
    }

    function hints(language) {
        var list = languages[language];

        return function (cm) {
            if (!list) {
                return null;
            }

            var cursor = cm.getCursor();
            var before = cm.getLine(cursor.line).slice(0, cursor.ch);
            var typed = /[\wЀ-ӿ.]*$/.exec(before)[0];
            var dot = typed.lastIndexOf(".");
            var from = cursor.ch - typed.length;
            var candidates;
            var prefix = typed;

            if (dot >= 0) {
                // "Console.Wr" → the known names that start with it; "text.Sp" → the common members
                var owner = typed.slice(0, dot + 1);
                var known = list.globals.filter(function (name) {
                    return startsWith(name, owner);
                });
                if (known.length > 0 && owner.length > 1) {
                    candidates = rank(known.filter(function (name) {
                        return startsWith(name, typed);
                    }), typed);
                } else {
                    prefix = typed.slice(dot + 1);
                    from = cursor.ch - prefix.length;
                    candidates = rank(list.members.filter(function (name) {
                        return startsWith(name, prefix);
                    }), prefix);
                }
            } else {
                if (typed.length === 0) {
                    return null;
                }
                var words = unique(programWords(cm, typed));
                candidates = unique(rank(
                    words.concat(list.keywords, list.globals).filter(function (name) {
                        return startsWith(name, typed) && name !== typed;
                    }),
                    typed));
            }

            // Nothing to add when the only suggestion is what is already typed
            candidates = candidates.filter(function (name) {
                return name !== prefix && name !== prefix + "(";
            });
            if (candidates.length === 0) {
                return null;
            }

            return {
                list: candidates.slice(0, 50).map(completion),
                from: { line: cursor.line, ch: from },
                to: cursor
            };
        };
    }

    function insideTextOrComment(cm) {
        var type = cm.getTokenAt(cm.getCursor()).type || "";
        return /string|comment/.test(type);
    }

    function show(cm, language) {
        cm.showHint({ hint: hints(language()), completeSingle: false });
    }

    // Turns hints on for an editor; language() returns the current language id
    window.SlCodeHints = {
        attach: function (cm, language) {
            cm.addKeyMap({
                "Ctrl-Space": function (editor) {
                    show(editor, language);
                }
            });
            cm.on("inputRead", function (editor, change) {
                if (change.origin !== "+input" || editor.state.completionActive || insideTextOrComment(editor)) {
                    return;
                }
                var typed = change.text[change.text.length - 1];
                if (/[\wЀ-ӿ.]$/.test(typed) && !/^\d+$/.test(typed)) {
                    show(editor, language);
                }
            });
        }
    };
})();
