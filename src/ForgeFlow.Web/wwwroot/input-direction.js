// Mixed-direction text entry: empty fields follow the page direction (caret on the right in RTL),
// while typed Latin letters and digits flow left-to-right and Persian text flows right-to-left.
(function () {
    var skipTypes = { checkbox: 1, radio: 1, hidden: 1, button: 1, submit: 1, reset: 1, file: 1, range: 1, color: 1, image: 1, date: 1, time: 1, 'datetime-local': 1 };

    function apply(el) {
        if (!el || !el.tagName) return;
        var tag = el.tagName;
        if (tag !== 'INPUT' && tag !== 'TEXTAREA') return;
        if (tag === 'INPUT' && skipTypes[(el.type || 'text').toLowerCase()]) return;
        if (el.closest && el.closest('[data-auth-login]')) { if (el.getAttribute('dir') !== 'ltr') el.setAttribute('dir', 'ltr'); return; }
        var pageDir = document.documentElement.getAttribute('dir') || 'ltr';
        var wanted = el.value ? 'auto' : pageDir;
        if (el.getAttribute('dir') !== wanted) el.setAttribute('dir', wanted);
    }

    function applyAll() {
        var nodes = document.querySelectorAll('input, textarea');
        for (var i = 0; i < nodes.length; i++) apply(nodes[i]);
    }

    document.addEventListener('focusin', function (e) { apply(e.target); }, true);
    document.addEventListener('input', function (e) { apply(e.target); }, true);
    document.addEventListener('DOMContentLoaded', applyAll);
    window.addEventListener('load', applyAll);
    document.addEventListener('enhancedload', applyAll);
})();
