// Helper GENERICO de arrastrar-y-soltar para reordenar listas (reutilizable en cualquier vista).
// Motivo (igual que pipeline-dnd.js): el DnD nativo no funciona con los @ondragover de Blazor
// Server porque el preventDefault del dragover debe ser SINCRONO en cliente y Blazor enruta los
// eventos al servidor async. Por eso el DnD vive aqui, a nivel document (la delegacion sobrevive
// a los re-render de Blazor), y se devuelve el movimiento a .NET.
//
// Convencion de marcado:
//   - Contenedor de la lista:  data-sortable="<grupo>" data-sortowner="<ownerId>"
//   - Cada item de la lista:    data-sortitem="<indice>"
//   - Agarradera (draggable):   data-sortgrip   (class vs-grip para el look de puntitos)
// El componente registra su DotNetObjectReference con visalSortable.register(ownerId, ref) y
// expone [JSInvokable] SortableMove(string grupo, int from, int to). "grupo" permite varias
// listas en un mismo componente (p.ej. varios niveles o varias hojas); "ownerId" permite varios
// componentes en una misma pagina. El arrastre se limita a items del mismo contenedor.
window.visalSortable = (function () {
    var owners = {}; // ownerId -> DotNetObjectReference
    var wired = false;
    var from = -1;
    var dragGroup = null;
    var dragOwner = null;

    function itemOf(t) { return t && t.closest ? t.closest('[data-sortitem]') : null; }
    function boxOf(t) { return t && t.closest ? t.closest('[data-sortable]') : null; }
    function sameBox(box) {
        return box && box.getAttribute('data-sortable') === dragGroup
            && (box.getAttribute('data-sortowner') || '') === dragOwner;
    }
    function clearHints() {
        document.querySelectorAll('.vs-drop-hint').forEach(function (c) { c.classList.remove('vs-drop-hint'); });
    }

    function wire() {
        if (wired) { return; }
        wired = true;

        document.addEventListener('dragstart', function (e) {
            var grip = e.target && e.target.closest ? e.target.closest('[data-sortgrip]') : null;
            if (!grip) { return; }
            var item = itemOf(grip);
            if (!item) { return; }
            var box = boxOf(item);
            if (!box) { return; }
            from = parseInt(item.getAttribute('data-sortitem'), 10);
            dragGroup = box.getAttribute('data-sortable');
            dragOwner = box.getAttribute('data-sortowner') || '';
            if (e.dataTransfer) {
                e.dataTransfer.effectAllowed = 'move';
                try { e.dataTransfer.setData('text/plain', String(from)); } catch (_) { }
                try { e.dataTransfer.setDragImage(item, 12, 12); } catch (_) { }
            }
            item.classList.add('vs-dragging');
        });

        document.addEventListener('dragend', function () {
            document.querySelectorAll('.vs-dragging').forEach(function (c) { c.classList.remove('vs-dragging'); });
            clearHints();
            from = -1; dragGroup = null; dragOwner = null;
        });

        document.addEventListener('dragover', function (e) {
            if (from < 0) { return; }
            var box = boxOf(e.target);
            if (!sameBox(box)) { return; }
            e.preventDefault(); // sincrono: habilita el drop
            if (e.dataTransfer) { e.dataTransfer.dropEffect = 'move'; }
            clearHints();
            var item = itemOf(e.target);
            if (item) { item.classList.add('vs-drop-hint'); }
        });

        document.addEventListener('drop', function (e) {
            if (from < 0) { return; }
            var box = boxOf(e.target);
            if (!sameBox(box)) { return; }
            e.preventDefault();
            var item = itemOf(e.target);
            var to = item ? parseInt(item.getAttribute('data-sortitem'), 10) : -1;
            var f = from, g = dragGroup, o = dragOwner;
            from = -1; dragGroup = null; dragOwner = null;
            clearHints();
            if (to < 0) { return; }
            var ref = owners[o];
            if (ref) { ref.invokeMethodAsync('SortableMove', g, f, to); }
        });
    }

    return {
        register: function (ownerId, ref) { owners[ownerId] = ref; wire(); },
        unregister: function (ownerId) { delete owners[ownerId]; }
    };
})();
