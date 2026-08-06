// Global HTML5 drag helper: mientras hay un drag activo desde la paleta del
// Form Builder, forzamos preventDefault() en TODOS los dragover del documento
// (fase de captura). Con esto el navegador acepta el drop en cualquier parte
// -- incluidos <button> nativos y elementos internos -- sin que aparezca el
// cursor "prohibido". Los handlers @ondrop de cada .fb-section, .fb-section-
// head y .fb-section-body siguen decidiendo dónde cae el nodo.
window.visalDragAllow = (function () {
    var handler = function (e) { e.preventDefault(); };
    var enabled = false;
    return {
        enable: function () {
            if (enabled) { return; }
            enabled = true;
            document.addEventListener('dragover', handler, true);
            document.addEventListener('dragenter', handler, true);
        },
        disable: function () {
            if (!enabled) { return; }
            enabled = false;
            document.removeEventListener('dragover', handler, true);
            document.removeEventListener('dragenter', handler, true);
        }
    };
})();
