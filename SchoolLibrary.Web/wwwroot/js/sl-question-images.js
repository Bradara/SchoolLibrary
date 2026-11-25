// "Вмъкни в текста" on a question's edit page: puts an [[img:N]] marker where the cursor was in the
// question text or in the task of a code question (whichever was used last), without saving the form.
(function () {
    'use strict';

    var buttons = document.querySelectorAll('[data-insert-marker]');
    var form = document.querySelector('form.question-form');
    if (!buttons.length || !form) {
        return;
    }

    var text = form.querySelector('[name="Text"]');
    var statement = form.querySelector('textarea[name="Statement"]');
    var last = null;

    [text, statement].forEach(function (field) {
        if (field) {
            field.addEventListener('focus', function () { last = field; });
        }
    });

    function isCode() {
        var type = form.querySelector('input[name="Type"]:checked');
        return type && type.value === 'Code';
    }

    // The field used last, if it is still visible; else the task of a code question or the text
    function target() {
        if (last && last.offsetParent !== null) {
            return last;
        }
        return isCode() && statement ? statement : text;
    }

    function insert(field, marker) {
        var start = field.selectionStart == null ? field.value.length : field.selectionStart;
        var end = field.selectionEnd == null ? start : field.selectionEnd;
        var value = field.value.slice(0, start) + marker + field.value.slice(end);
        var max = field.maxLength > 0 ? field.maxLength : Infinity;
        if (value.length > max) {
            field.setCustomValidity('Няма място за маркера: текстът е твърде дълъг.');
            field.reportValidity();
            field.setCustomValidity('');
            return;
        }

        field.value = value;
        field.focus();
        field.setSelectionRange(start + marker.length, start + marker.length);
        field.dispatchEvent(new Event('input', { bubbles: true }));
    }

    buttons.forEach(function (button) {
        button.hidden = false;
        // Keep the cursor of the text field: the button does not take the focus on mouse down
        button.addEventListener('mousedown', function (e) { e.preventDefault(); });
        button.addEventListener('click', function () {
            var field = target();
            if (field) {
                insert(field, button.getAttribute('data-insert-marker'));
            }
        });
    });
})();
