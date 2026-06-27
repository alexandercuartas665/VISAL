// form-voice.js -- Dictado por voz para textareas con data-voice-target.
//
// Implementacion via Web Speech API nativa del navegador (Chrome, Edge).
//   - Sin servidor de transcripcion (no se envia audio a tu backend).
//   - Interim results en gris en el popover (mientras el reconocedor "piensa").
//   - Cada resultado FINAL se appendea al textarea y dispara 'change' para que
//     el autosave del FormViewer corra.
//   - La sesion se corta cada ~30-60s; auto-restart silencioso si no esta pausada.
//   - Cada 10 palabras finales se dispara un 'input' adicional para forzar el
//     autosave del FormViewer aunque su debounce sea largo.

window.visalVoice = (function () {
  const ATTACHED = new WeakSet();
  let popover = null;
  let session = null; // { recognition, textarea, btn, paused, wordsSinceFlush, interimText, finalSoFar }

  function getSR() {
    return window.SpeechRecognition || window.webkitSpeechRecognition || null;
  }

  function setup() {
    document.querySelectorAll('button[data-voice-target]').forEach(btn => {
      if (ATTACHED.has(btn)) { return; }
      ATTACHED.add(btn);
      btn.addEventListener('click', onButtonClick);
    });
  }

  function onButtonClick(e) {
    e.preventDefault();
    const btn = e.currentTarget;
    const targetId = btn.dataset.voiceTarget;
    const textarea = document.getElementById(targetId);
    if (!textarea) { return; }

    if (session && session.textarea === textarea) { stopSession(); return; }
    if (session) { stopSession(); }

    const SR = getSR();
    if (!SR) {
      alert('Tu navegador no soporta dictado por voz. Usa Chrome o Edge.');
      return;
    }
    startSession(textarea, btn, SR);
  }

  function startSession(textarea, btn, SR) {
    const r = new SR();
    r.lang = 'es-CO';
    r.continuous = true;
    r.interimResults = true;
    r.maxAlternatives = 1;

    session = {
      recognition: r,
      textarea,
      btn,
      paused: false,
      stopping: false,
      wordsSinceFlush: 0,
      interimText: ''
    };

    r.onresult = onResult;
    r.onerror = onError;
    r.onend = onEnd;
    r.onstart = () => setStatus('Escuchando...');
    r.onspeechstart = () => setStatus('Escuchando...');

    try { r.start(); }
    catch (err) { alert('No se pudo iniciar el dictado: ' + (err?.message || err)); session = null; return; }

    btn.classList.add('recording');
    openPopover();
  }

  function onResult(e) {
    if (!session) { return; }
    // En cada evento puede llegar varios resultados; los nuevos finales se
    // appendean al textarea, los interim se acumulan para mostrar en el
    // popover (en gris cursiva) hasta que se conviertan en finales.
    let newFinal = '';
    let interim = '';
    for (let i = e.resultIndex; i < e.results.length; i++) {
      const res = e.results[i];
      const txt = res[0].transcript;
      if (res.isFinal) { newFinal += txt; }
      else { interim += txt; }
    }
    session.interimText = interim.trim();
    if (newFinal.trim()) { appendFinal(newFinal.trim()); }
    updateInterim();
    updateWordCount();
  }

  function onError(e) {
    if (!session) { return; }
    // Errores recuperables: dejamos que onend reinicie si no estamos pausados.
    if (e.error === 'no-speech' || e.error === 'aborted') { return; }
    if (e.error === 'not-allowed' || e.error === 'service-not-allowed') {
      setStatus('Microfono denegado.', 'err');
      stopSession();
      return;
    }
    if (e.error === 'network') {
      setStatus('Red interrumpida, reintentando...', 'err');
      return;
    }
    setStatus('Error: ' + e.error, 'err');
  }

  function onEnd() {
    if (!session) { return; }
    // La API corta cada ~30-60s sin importar si seguimos hablando. Re-arrancamos
    // silenciosamente si no estamos pausados ni cerrando explicitamente.
    if (session.stopping || session.paused) { return; }
    try { session.recognition.start(); }
    catch (_) { /* ya inicio o se cerro */ }
  }

  function appendFinal(text) {
    if (!session) { return; }
    const t = session.textarea;
    const cur = t.value || '';
    const sep = cur.length > 0 && !cur.endsWith(' ') && !cur.endsWith('\n') ? ' ' : '';
    t.value = cur + sep + text;
    // El binding @onchange del FormViewer captura este evento.
    t.dispatchEvent(new Event('change', { bubbles: true }));

    const newWords = text.split(/\s+/).filter(Boolean).length;
    session.wordsSinceFlush += newWords;
    if (session.wordsSinceFlush >= 10) {
      session.wordsSinceFlush = 0;
      t.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }

  function stopSession() {
    if (!session) { return; }
    const s = session;
    s.stopping = true;
    try { s.recognition.stop(); } catch (_) {}
    session = null;
    s.btn.classList.remove('recording');
    closePopover();
  }

  function togglePause() {
    if (!session) { return; }
    session.paused = !session.paused;
    if (session.paused) {
      try { session.recognition.stop(); } catch (_) {}
      setStatus('Pausado.', 'paused');
      session.btn.classList.remove('recording');
    } else {
      try { session.recognition.start(); } catch (_) {}
      setStatus('Escuchando...');
      session.btn.classList.add('recording');
    }
    document.getElementById('fv-voice-pause').textContent = session.paused ? 'Continuar' : 'Pausar';
  }

  // ===== Popover (singleton) =====

  function ensurePopover() {
    if (popover) { return popover; }
    popover = document.createElement('div');
    popover.className = 'fv-voice-pop';
    popover.innerHTML = `
      <div class="fv-voice-pop-h">
        <span class="fv-voice-mic">&#127908;</span>
        <span id="fv-voice-status">Iniciando...</span>
      </div>
      <div class="fv-voice-interim" id="fv-voice-interim"></div>
      <div class="fv-voice-pop-count" id="fv-voice-count">0 palabras dictadas</div>
      <div class="fv-voice-pop-actions">
        <button type="button" class="fv-voice-pop-btn" id="fv-voice-pause">Pausar</button>
        <button type="button" class="fv-voice-pop-btn fv-voice-pop-btn-stop" id="fv-voice-stop">Detener</button>
      </div>`;
    document.body.appendChild(popover);
    document.getElementById('fv-voice-pause').addEventListener('click', togglePause);
    document.getElementById('fv-voice-stop').addEventListener('click', stopSession);
    injectStyles();
    return popover;
  }

  function openPopover() {
    ensurePopover();
    popover.classList.add('open');
    document.getElementById('fv-voice-pause').textContent = 'Pausar';
    updateInterim();
    updateWordCount();
  }
  function closePopover() {
    if (popover) { popover.classList.remove('open'); }
  }
  function setStatus(msg, kind) {
    if (!popover) { return; }
    const el = document.getElementById('fv-voice-status');
    if (el) {
      el.textContent = msg;
      el.className = kind === 'err' ? 'fv-voice-status-err' :
                     kind === 'paused' ? 'fv-voice-status-paused' :
                     'fv-voice-status-ok';
    }
  }
  function updateInterim() {
    if (!popover) { return; }
    const el = document.getElementById('fv-voice-interim');
    if (el) { el.textContent = session?.interimText || ''; }
  }
  function updateWordCount() {
    if (!popover || !session) { return; }
    const c = document.getElementById('fv-voice-count');
    if (c) {
      const total = (session.textarea.value || '').split(/\s+/).filter(Boolean).length;
      c.textContent = total + ' palabras dictadas';
    }
  }

  function injectStyles() {
    if (document.getElementById('fv-voice-styles')) { return; }
    const s = document.createElement('style');
    s.id = 'fv-voice-styles';
    s.textContent = `
.fv-voice-pop {
  position: fixed; right: 24px; bottom: 24px; z-index: 9000;
  background: #ffffff; border: 1px solid #cbd5e1; border-radius: 12px;
  box-shadow: 0 12px 32px rgba(15,23,42,.25);
  width: 300px; padding: 14px; display: none; font-family: inherit;
}
.fv-voice-pop.open { display: block; }
.fv-voice-pop-h {
  display: flex; align-items: center; gap: 10px;
  font-size: 14px; font-weight: 600; color: #334155; margin-bottom: 8px;
}
.fv-voice-mic { font-size: 22px; }
#fv-voice-status.fv-voice-status-ok { color: #1565c0; }
#fv-voice-status.fv-voice-status-err { color: #b91c1c; }
#fv-voice-status.fv-voice-status-paused { color: #64748b; }
.fv-voice-interim {
  min-height: 28px; max-height: 56px; overflow-y: auto;
  background: #f8fafc; border-radius: 6px; padding: 6px 8px;
  font-size: 12px; font-style: italic; color: #64748b; line-height: 1.4;
  margin-bottom: 6px;
}
.fv-voice-interim:empty::before { content: 'Aqui aparece lo que se va escuchando...'; opacity: .55; }
.fv-voice-pop-count {
  font-size: 11px; color: #64748b; padding: 4px 0 12px;
  border-bottom: 1px solid #e2e8f0; margin-bottom: 12px;
}
.fv-voice-pop-actions { display: flex; gap: 8px; }
.fv-voice-pop-btn {
  flex: 1; padding: 6px 10px; font-size: 12.5px; border: 1px solid #cbd5e1;
  background: #f1f5f9; color: #334155; border-radius: 6px; cursor: pointer;
}
.fv-voice-pop-btn:hover { background: #e2e8f0; }
.fv-voice-pop-btn-stop { background: #dc2626; color: #fff; border-color: #b91c1c; }
.fv-voice-pop-btn-stop:hover { background: #b91c1c; }
`;
    document.head.appendChild(s);
  }

  return { setup };
})();
