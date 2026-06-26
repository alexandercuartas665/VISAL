// form-toc.js — navegacion lateral por secciones dentro del FormViewer.
//
// Construye un IntersectionObserver por cada .fv-shell del DOM. Las secciones
// llevan data-toc-section="<id>" y las entradas del TOC data-toc-link="<id>".
// Click en una entrada → scroll suave al ancla; conforme el usuario scrollea,
// la entrada de la seccion mas visible se marca .active. El FAB (boton movil)
// abre/cierra el panel cuando el sidebar esta colapsado.
//
// El setup es idempotente: el FormViewer lo invoca despues de cada render via
// JSInterop, asi que cambiar de documento (de Barthel a Norton, p.ej.) re-arma
// los observers contra el nuevo arbol DOM. Los observers anteriores se
// desconectan antes para no acumular handlers fantasma.

window.visalFormToc = (function () {
  const REGISTRY = new WeakMap(); // shell -> { handlers[], scrollTarget, onScroll }

  function setup() {
    document.querySelectorAll('.fv-shell').forEach(setupOne);
  }

  // Sube por el DOM buscando un ancestro que efectivamente scrollee verticalmente
  // (overflow auto/scroll y contenido mas alto que el viewport del elemento).
  // Esto importa para modales: el viewport global no se mueve, pero el modal si.
  function findScrollAncestor(el) {
    let cur = el.parentElement;
    while (cur && cur !== document.body) {
      const cs = getComputedStyle(cur);
      const oy = cs.overflowY;
      if ((oy === 'auto' || oy === 'scroll' || oy === 'overlay')
          && cur.scrollHeight > cur.clientHeight) {
        return cur;
      }
      cur = cur.parentElement;
    }
    return null;
  }

  function setupOne(shell) {
    // Limpieza idempotente: si habia spy registrado para este shell, quitar
    // el listener de scroll/resize y todos los click handlers anteriores.
    const prev = REGISTRY.get(shell);
    if (prev) {
      window.removeEventListener('scroll', prev.onScroll, { capture: true });
      window.removeEventListener('resize', prev.onScroll);
      prev.handlers.forEach(({ el, fn }) => el.removeEventListener('click', fn));
    }

    const sections = shell.querySelectorAll('[data-toc-section]');
    const links = shell.querySelectorAll('[data-toc-link]');
    if (sections.length === 0 || links.length === 0) {
      REGISTRY.delete(shell);
      return;
    }

    const linkById = {};
    links.forEach(l => { linkById[l.dataset.tocLink] = l; });

    // Refs al panel del TOC y al FAB (puede no existir si el shell es chico).
    // Se declaran arriba para que updateActive las tenga en su closure.
    const toc = shell.querySelector('[data-toc-root]');
    const fab = shell.querySelector('[data-toc-fab]');

    // El FormViewer suele estar dentro de un modal con su propio scroller.
    // IntersectionObserver con root=elemento se porto erratico contra ese
    // contenedor (no disparaba entries iniciales), asi que usamos scroll
    // listeners + getBoundingClientRect — funciona de forma identica en
    // viewport y en modal, y es trivialmente predecible.
    const scrollRoot = findScrollAncestor(shell) || null;

    function updateActive() {
      // Punto de referencia: 25% desde el tope del area visible. La seccion
      // cuyo top este mas cerca por encima de ese pivote (sin pasarse) es la
      // activa. Asi la activacion se siente "anclada" al header del documento.
      const refRect = scrollRoot ? scrollRoot.getBoundingClientRect()
                                 : { top: 0, bottom: window.innerHeight };
      const pivot = refRect.top + (refRect.bottom - refRect.top) * 0.25;
      let best = null;
      let bestDelta = Infinity;
      for (const s of sections) {
        const r = s.getBoundingClientRect();
        const delta = pivot - r.top;
        // delta>=0: la seccion ya paso el pivote (esta arriba o sobre el).
        // Elegimos la que minimice delta positivo (la mas cercana por encima).
        if (delta >= -8 && delta < bestDelta) {
          best = s;
          bestDelta = delta;
        }
      }
      if (!best) { best = sections[0]; }
      const link = linkById[best.dataset.tocSection];
      if (!link || link.classList.contains('active')) { return; }
      links.forEach(l => l.classList.remove('active'));
      link.classList.add('active');
      // Mantener la entrada activa visible dentro del TOC. Evitamos
      // scrollIntoView (puede bubble-up y resetear el scroll del modal):
      // ajustamos scrollTop del propio TOC solo cuando el link se sale.
      if (toc) {
        const lr = link.getBoundingClientRect();
        const tr = toc.getBoundingClientRect();
        if (lr.top < tr.top) {
          toc.scrollTop += lr.top - tr.top - 4;
        } else if (lr.bottom > tr.bottom) {
          toc.scrollTop += lr.bottom - tr.bottom + 4;
        }
      }
    }

    // rAF dedupe para no thrasher el layout durante scrolls largos.
    let rafScheduled = false;
    function onScroll() {
      if (rafScheduled) { return; }
      rafScheduled = true;
      requestAnimationFrame(() => { rafScheduled = false; updateActive(); });
    }
    // Capturar scroll en TODO el documento (capture:true) en vez de atarse al
    // ancestor scrolleable identificado en setup. Razon: cuando el FormViewer
    // monta dentro de un modal, el ancestor puede no ser todavia scrolleable
    // (sin contenido prefill), entonces findScrollAncestor devuelve null y nos
    // ataríamos a window — un scroll posterior en el modal nunca dispararia.
    // Con capture:true cualquier scroll en cualquier elemento llega aqui.
    window.addEventListener('scroll', onScroll, { capture: true, passive: true });
    window.addEventListener('resize', onScroll);
    // Marcar el estado inicial sin esperar al primer scroll.
    updateActive();
    // Recalcular en 300ms para cubrir el caso de que la altura del contenedor
    // haya cambiado por prefill async — asi el pivote inicial es correcto.
    setTimeout(updateActive, 300);

    const handlers = [];

    // Click en una entrada del TOC → scroll suave al ancla.
    links.forEach(l => {
      const fn = (e) => {
        e.preventDefault();
        const id = l.dataset.tocLink;
        const target = shell.querySelector(`[data-toc-section="${id}"]`);
        if (target) {
          target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
        // En movil/colapsado, cerrar el panel tras elegir.
        if (toc) { toc.classList.remove('fv-toc-open'); }
      };
      l.addEventListener('click', fn);
      handlers.push({ el: l, fn });
    });

    // FAB (visible solo en pantallas chicas via CSS) abre/cierra el panel.
    if (fab && toc) {
      const fn = () => toc.classList.toggle('fv-toc-open');
      fab.addEventListener('click', fn);
      handlers.push({ el: fab, fn });
    }

    REGISTRY.set(shell, { handlers, onScroll });
  }

  return { setup };
})();
