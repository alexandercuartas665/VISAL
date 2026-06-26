// form-toc.js — TOC del FormViewer: boton flotante sticky + popover con
// scroll-spy. Cuando el form tiene 3+ secciones top-level, FormViewer mete
// un boton "Secciones" en la esquina superior derecha. Click toggle el
// popover; click en un link hace scroll suave a la seccion y cierra. El
// scroll del documento (incluyendo modal con scroller propio) actualiza
// la entrada activa via getBoundingClientRect contra un pivote al 25%
// del area visible.
//
// El setup es idempotente: FormViewer lo invoca despues de cada render via
// JSInterop; el cleanup remueve listeners de shells reusados antes de
// re-engancharlos.

window.visalFormToc = (function () {
  const REGISTRY = new WeakMap(); // shell -> { handlers[], onScroll, onDocClick }

  function setup() {
    document.querySelectorAll('.fv-shell').forEach(setupOne);
  }

  // Sube por el DOM buscando el ancestro scrolleable real. Importa cuando el
  // FormViewer vive dentro de un modal con su propio scroller: usamos su
  // rect como referencia del pivote.
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
    const prev = REGISTRY.get(shell);
    if (prev) {
      window.removeEventListener('scroll', prev.onScroll, { capture: true });
      window.removeEventListener('resize', prev.onScroll);
      document.removeEventListener('click', prev.onDocClick, true);
      prev.handlers.forEach(({ el, fn, ev }) => el.removeEventListener(ev || 'click', fn));
    }

    const sections = shell.querySelectorAll('[data-toc-section]');
    const links = shell.querySelectorAll('[data-toc-link]');
    const toggle = shell.querySelector('[data-toc-toggle]');
    const pop = shell.querySelector('[data-toc-pop]');
    const floater = shell.querySelector('[data-toc-floater]');
    if (sections.length === 0 || links.length === 0 || !toggle || !pop || !floater) {
      REGISTRY.delete(shell);
      return;
    }

    const linkById = {};
    links.forEach(l => { linkById[l.dataset.tocLink] = l; });

    const scrollRoot = findScrollAncestor(shell) || null;

    // Reanclar el boton flotante al viewport del scroll-ancestor del shell
    // (top-right del modal). Sin esto, position:fixed lo dejaria en el top-
    // right del viewport global, ignorando que el form vive en un modal.
    function updateFloaterPos() {
      if (!scrollRoot) {
        floater.style.top = '8px';
        floater.style.right = '16px';
        return;
      }
      const sr = scrollRoot.getBoundingClientRect();
      floater.style.top = Math.max(8, sr.top + 8) + 'px';
      floater.style.right = Math.max(16, window.innerWidth - sr.right + 16) + 'px';
    }

    function updateActive() {
      const refRect = scrollRoot ? scrollRoot.getBoundingClientRect()
                                 : { top: 0, bottom: window.innerHeight };
      const pivot = refRect.top + (refRect.bottom - refRect.top) * 0.25;
      let best = null;
      let bestDelta = Infinity;
      for (const s of sections) {
        const r = s.getBoundingClientRect();
        const delta = pivot - r.top;
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
      // Si el popover esta abierto, asegurar la entrada activa visible.
      if (pop.classList.contains('open')) {
        const lr = link.getBoundingClientRect();
        const pr = pop.getBoundingClientRect();
        if (lr.top < pr.top) { pop.scrollTop += lr.top - pr.top - 4; }
        else if (lr.bottom > pr.bottom) { pop.scrollTop += lr.bottom - pr.bottom + 4; }
      }
    }

    let rafScheduled = false;
    function onScroll() {
      if (rafScheduled) { return; }
      rafScheduled = true;
      requestAnimationFrame(() => {
        rafScheduled = false;
        updateActive();
        updateFloaterPos();
      });
    }
    // capture:true cubre el caso de modal con scroller propio (un scroll en
    // ese contenedor no burbujea, pero si se captura en window).
    window.addEventListener('scroll', onScroll, { capture: true, passive: true });
    window.addEventListener('resize', onScroll);
    updateFloaterPos();
    updateActive();
    setTimeout(() => { updateFloaterPos(); updateActive(); }, 300);

    const handlers = [];

    function closePop() {
      pop.classList.remove('open');
      toggle.classList.remove('open');
    }
    function openPop() {
      pop.classList.add('open');
      toggle.classList.add('open');
      // Recalcular activo al abrir para que el highlight refleje scroll
      // que paso mientras el popover estaba cerrado.
      updateActive();
    }
    function togglePop(e) {
      e.preventDefault();
      e.stopPropagation();
      if (pop.classList.contains('open')) { closePop(); } else { openPop(); }
    }
    toggle.addEventListener('click', togglePop);
    handlers.push({ el: toggle, fn: togglePop });

    // Click fuera del popover lo cierra. Capture:true asegura que llegue
    // antes que otros handlers que pudieran cancelar la burbuja.
    function onDocClick(e) {
      if (!pop.classList.contains('open')) { return; }
      if (toggle.contains(e.target) || pop.contains(e.target)) { return; }
      closePop();
    }
    document.addEventListener('click', onDocClick, true);

    // Click en un link: scroll suave a la seccion y cerrar.
    links.forEach(l => {
      const fn = (e) => {
        e.preventDefault();
        const id = l.dataset.tocLink;
        const target = shell.querySelector(`[data-toc-section="${id}"]`);
        if (target) {
          target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
        closePop();
      };
      l.addEventListener('click', fn);
      handlers.push({ el: l, fn });
    });

    REGISTRY.set(shell, { handlers, onScroll, onDocClick });
  }

  return { setup };
})();
