// Drag-and-drop de las filas del gestor de Campos Dinamicos del Paciente (modal en /admision).
// Mismo motivo y patron que reportes-dnd.js / pipeline-dnd.js: el DnD nativo no funciona con los
// @ondragover de Blazor Server (preventDefault debe ser sincrono en cliente), asi que lo hacemos
// en JS a nivel document y devolvemos el movimiento a .NET. Reordena fijos + dinamicos; el guardado
// real (ReordenarTabAsync, dos tablas) lo hace el componente en MoverFilaFromJs.
window.visalCamposDnd = (function () {
    var dotnet = null;
    var wired = false;
    var from = -1;

    function rowOf(t) { return t && t.closest ? t.closest('tr[data-row]') : null; }
    function boxOf(t) { return t && t.closest ? t.closest('[data-cdcampos]') : null; }
    function clearHints() {
        document.querySelectorAll('tr.cd-drop-hint').forEach(function (c) { c.classList.remove('cd-drop-hint'); });
    }

    function wire() {
        if (wired) { return; }
        wired = true;

        document.addEventListener('dragstart', function (e) {
            var row = rowOf(e.target);
            if (!row || !boxOf(row)) { return; }
            from = parseInt(row.getAttribute('data-row'), 10);
            if (e.dataTransfer) {
                e.dataTransfer.effectAllowed = 'move';
                try { e.dataTransfer.setData('text/plain', String(from)); } catch (_) { }
                // Usa la fila completa como imagen de arrastre (no solo el grip).
                try { e.dataTransfer.setDragImage(row, 12, 12); } catch (_) { }
            }
            row.classList.add('cd-row-dragging');
        });

        document.addEventListener('dragend', function (e) {
            var row = rowOf(e.target);
            if (row) { row.classList.remove('cd-row-dragging'); }
            clearHints();
            from = -1;
        });

        document.addEventListener('dragover', function (e) {
            if (from < 0) { return; }
            if (!boxOf(e.target)) { return; }
            e.preventDefault(); // sincrono: habilita el drop
            if (e.dataTransfer) { e.dataTransfer.dropEffect = 'move'; }
            clearHints();
            var row = rowOf(e.target);
            if (row) { row.classList.add('cd-drop-hint'); }
        });

        document.addEventListener('drop', function (e) {
            if (from < 0) { return; }
            if (!boxOf(e.target)) { return; }
            e.preventDefault();
            var row = rowOf(e.target);
            var to = row ? parseInt(row.getAttribute('data-row'), 10) : -1;
            var f = from;
            from = -1;
            clearHints();
            if (to < 0) { return; }
            if (dotnet) { dotnet.invokeMethodAsync('MoverFilaFromJs', f, to); }
        });
    }

    return {
        init: function (ref) { dotnet = ref; wire(); }
    };
})();
