// Posiciona el menu del kebab de Ordenes Clinicas como 'fixed' para que NO lo recorte
// el overflow del contenedor de la tabla. Pasaba con pocas filas (el wrap queda bajito)
// o con filas al fondo del viewport. Solo hay un kebab abierto a la vez, asi que basta
// con buscar el unico .oc-kebab-menu visible y anclarlo a su boton.
window.ocKebab = {
  place: function () {
    try {
      var menu = document.querySelector('.oc-kebab-menu');
      if (!menu) { return; }
      var wrap = menu.closest('.oc-kebab-wrap');
      var btn = wrap ? wrap.querySelector('.oc-kebab-btn') : null;
      if (!btn) { return; }

      var r = btn.getBoundingClientRect();
      menu.style.position = 'fixed';
      menu.style.margin = '0';
      // Reset para medir el tamano real del menu.
      menu.style.top = '0px';
      menu.style.left = '0px';
      var mh = menu.offsetHeight, mw = menu.offsetWidth;

      // Por defecto despliega hacia abajo; si no cabe, hacia arriba.
      var top = r.bottom + 4;
      if (top + mh > window.innerHeight - 8) { top = r.top - mh - 4; }
      if (top < 8) { top = 8; }

      // Alinea a la izquierda del boton; si se sale por la derecha, lo corre.
      var left = r.left;
      if (left + mw > window.innerWidth - 8) { left = window.innerWidth - mw - 8; }
      if (left < 8) { left = 8; }

      menu.style.top = top + 'px';
      menu.style.left = left + 'px';
      menu.style.zIndex = '2000';
    } catch (e) { /* noop */ }
  }
};
