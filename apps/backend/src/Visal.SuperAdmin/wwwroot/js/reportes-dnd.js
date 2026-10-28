// Drag-and-drop de las listas ordenables del Constructor de Reportes (columnas del
// modo Detalle, campos de "Agrupar por" y filas de "Agregados" del modo Agregado).
// El DnD nativo de HTML5 no funciona con los @ondragover de Blazor Server porque el
// preventDefault() del dragover debe ser SINCRONO en el cliente y Blazor enruta los
// eventos al servidor de forma asincrona (cuando vuelve el preventDefault, el navegador
// ya descarto la zona de soltado y ni siquiera arranca el drag). Por eso lo hacemos en
// JS a nivel document (la delegacion sobrevive a los re-render de Blazor) y devolvemos
// el movimiento a .NET via invokeMethodAsync. Mismo patron que pipeline-dnd.js.
//
// Contrato de marcado: el contenedor lleva data-rcsort="<tipo>" data-hoja="<indice>"
// (tipo = col | grp | agg), cada fila lleva data-rcidx="<indice>" y el grip (.rc-grip,
// draggable="true") es lo unico arrastrable. Solo reordena dentro de la misma lista/hoja.
window.visalReportesDnd = (function () {
    var dotnet = null;
    var wired = false;
    var dragKind = null; // data-rcsort de la lista donde empezo el arrastre
    var dragHoja = null; // data-hoja de esa lista
    var dragFrom = -1;    // indice del item arrastrado

    function rowOf(t) { return t && t.closest ? t.closest('[data-rcidx]') : null; }
    function boxOf(t) { return t && t.closest ? t.closest('[data-rcsort]') : null; }
    function clearHints() {
        document.querySelectorAll('.rc-drop-hint').forEach(function (c) { c.classList.remove('rc-drop-hint'); });
    }
    function sameBox(box) {
        return !!box && box.getAttribute('data-rcsort') === dragKind && box.getAttribute('data-hoja') === dragHoja;
    }

    function wire() {
        if (wired) { return; }
        wired = true;

        document.addEventListener('dragstart', function (e) {
            var row = rowOf(e.target);
            if (!row) { return; }
            var box = boxOf(row);
            if (!box) { return; }
            dragKind = box.getAttribute('data-rcsort');
            dragHoja = box.getAttribute('data-hoja');
            dragFrom = parseInt(row.getAttribute('data-rcidx'), 10);
            if (e.dataTransfer) {
                e.dataTransfer.effectAllowed = 'move';
                // Setear datos es lo que hace que el navegador (Firefox incluido) ARRANQUE el drag.
                try { e.dataTransfer.setData('text/plain', String(dragFrom)); } catch (_) { }
            }
            row.classList.add('rc-dragging');
        });

        document.addEventListener('dragend', function (e) {
            var row = rowOf(e.target);
            if (row) { row.classList.remove('rc-dragging'); }
            clearHints();
            dragKind = null;
            dragHoja = null;
            dragFrom = -1;
        });

        document.addEventListener('dragover', function (e) {
            if (dragFrom < 0) { return; }
            var box = boxOf(e.target);
            if (!sameBox(box)) { return; } // solo dentro de la misma lista y hoja
            e.preventDefault(); // sincrono: habilita el drop
            if (e.dataTransfer) { e.dataTransfer.dropEffect = 'move'; }
            clearHints();
            var row = rowOf(e.target);
            if (row) { row.classList.add('rc-drop-hint'); }
        });

        document.addEventListener('drop', function (e) {
            if (dragFrom < 0) { return; }
            var box = boxOf(e.target);
            if (!sameBox(box)) { return; }
            e.preventDefault();
            var row = rowOf(e.target);
            var to = row ? parseInt(row.getAttribute('data-rcidx'), 10) : -1;
            var from = dragFrom;
            var kind = dragKind;
            var hoja = dragHoja;
            dragKind = null;
            dragHoja = null;
            dragFrom = -1;
            clearHints();
            if (to < 0 || hoja === null) { return; }
            if (dotnet) { dotnet.invokeMethodAsync('MoverItemFromJs', parseInt(hoja, 10), kind, from, to); }
        });
    }

    return {
        init: function (ref) { dotnet = ref; wire(); }
    };
})();
