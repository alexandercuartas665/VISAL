// sn-dualscroll.js
// Barra de scroll horizontal ARRIBA de una tabla, sincronizada con la barra
// de abajo (el contenedor con overflow real). Se usa en el modal "Ver snapshot"
// de Facturacion para que se puedan ver todas las columnas sin bajar al final.
//
// Uso desde Blazor:  JS.InvokeVoidAsync("snDualScroll.wire", "snVerTop", "snVerWrap")
//   topId : id del contenedor superior (solo tiene un spacer que fuerza el ancho)
//   wrapId: id del contenedor de la tabla (overflow:auto real)
// Es idempotente: se puede llamar en cada render; solo cablea los listeners una vez
// y refresca el ancho del spacer segun el ancho real de la tabla.
window.snDualScroll = {
    wire: function (topId, wrapId) {
        var top = document.getElementById(topId);
        var wrap = document.getElementById(wrapId);
        if (!top || !wrap) { return; }
        var table = wrap.querySelector('table');
        if (!table) { return; }
        var spacer = top.firstElementChild;

        var setWidth = function () {
            if (spacer) { spacer.style.width = table.scrollWidth + 'px'; }
        };
        setWidth();

        if (wrap.dataset.snDual === '1') { return; } // ya cableado
        wrap.dataset.snDual = '1';

        var syncing = false;
        top.addEventListener('scroll', function () {
            if (syncing) { return; }
            syncing = true;
            wrap.scrollLeft = top.scrollLeft;
            syncing = false;
        });
        wrap.addEventListener('scroll', function () {
            if (syncing) { return; }
            syncing = true;
            top.scrollLeft = wrap.scrollLeft;
            syncing = false;
        });

        // Mantener el ancho del spacer al dia si la tabla cambia (paginacion,
        // columnas, resize de ventana).
        if (window.ResizeObserver) {
            var ro = new ResizeObserver(setWidth);
            ro.observe(table);
        }
        window.addEventListener('resize', setWidth);
    }
};
