// form-voice.js -- Dictado por voz para textareas con data-voice-target.
//
// Flujo:
//   1) Click en boton -> pedir microfono y abrir el popover.
//   2) MediaRecorder graba chunks de 5s en audio/webm.
//   3) Cada 5s: stop -> blob -> fetch POST /api/transcribe -> restart.
//   4) Texto que devuelve Whisper se appendea al textarea (en la posicion del
//      caret si es accesible, sino al final). Disparamos un evento 'change'
//      para que el binding @onchange de Blazor capture y dispare el autosave.
//   5) Cada 10 palabras finalizadas, fuerza un dispatch extra (para que el
//      autosave del FormViewer corra antes del tope de debounce).
//
// El popover es un singleton sobre <body>; lo crea perezosamente la primera
// vez que alguien dicta.

window.visalVoice = (function () {
  const ATTACHED = new WeakSet(); // botones ya enganchados (evita doble-click handler)
  let popover = null;
  let session = null; // { mediaRecorder, stream, chunks, textarea, timer, paused, wordsSinceFlush }

  function setup() {
    document.querySelectorAll('button[data-voice-target]').forEach(btn => {
      if (ATTACHED.has(btn)) { return; }
      ATTACHED.add(btn);
      btn.addEventListener('click', onButtonClick);
    });
  }

  async function onButtonClick(e) {
    e.preventDefault();
    const btn = e.currentTarget;
    const targetId = btn.dataset.voiceTarget;
    const textarea = document.getElementById(targetId);
    if (!textarea) {
      alert('No se encontro el campo destino para el dictado.');
      return;
    }
    // Si ya hay sesion activa apuntando a este textarea, detener.
    if (session && session.textarea === textarea) {
      await stopSession();
      return;
    }
    // Si hay sesion en otro textarea, detenerla primero.
    if (session) { await stopSession(); }
    await startSession(textarea, btn);
  }

  async function startSession(textarea, btn) {
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
      alert('No se pudo acceder al microfono: ' + (err?.message || err));
      return;
    }
    const supportsWebm = MediaRecorder.isTypeSupported('audio/webm');
    const mime = supportsWebm ? 'audio/webm' : 'audio/ogg';
    const recorder = new MediaRecorder(stream, { mimeType: mime, audioBitsPerSecond: 32000 });
    session = {
      stream, recorder,
      chunks: [],
      textarea,
      btn,
      timer: null,
      paused: false,
      wordsSinceFlush: 0,
      mime,
      ext: mime.includes('webm') ? 'webm' : 'ogg',
      lastSendAt: Date.now()
    };
    recorder.ondataavailable = (e) => {
      if (e.data && e.data.size > 0) { session.chunks.push(e.data); }
    };
    recorder.onstop = handleRecorderStop;
    recorder.start();
    btn.classList.add('recording');
    openPopover();
    setStatus('Escuchando...');
    // Cada 5 segundos cerramos el chunk: stop -> ondataavailable -> onstop ->
    // restart. El upload del blob lo dispara handleRecorderStop.
    session.timer = setInterval(() => {
      if (!session || session.paused) { return; }
      if (session.recorder.state === 'recording') {
        session.recorder.stop(); // dispara onstop con los chunks acumulados
      }
    }, 5000);
  }

  function handleRecorderStop() {
    if (!session) { return; }
    const chunks = session.chunks;
    session.chunks = [];
    if (chunks.length === 0) {
      maybeRestart();
      return;
    }
    const blob = new Blob(chunks, { type: session.mime });
    // Reiniciamos el recorder antes de subir el blob viejo, asi no perdemos
    // segundos de audio mientras Whisper procesa.
    maybeRestart();
    uploadChunk(blob).catch(err => {
      setStatus('Error: ' + (err?.message || err), 'err');
    });
  }

  function maybeRestart() {
    if (!session || session.paused) { return; }
    try { session.recorder.start(); }
    catch (_) { /* recorder ya cerrado */ }
  }

  async function uploadChunk(blob) {
    setStatus('Procesando...');
    const fd = new FormData();
    fd.append('file', blob, `chunk.${session.ext}`);
    fd.append('lang', 'es');
    let resp;
    try { resp = await fetch('/api/transcribe', { method: 'POST', body: fd, credentials: 'same-origin' }); }
    catch (err) { setStatus('Red: ' + err.message, 'err'); return; }
    if (!resp.ok) { setStatus(`HTTP ${resp.status}`, 'err'); return; }
    const j = await resp.json();
    if (!j.ok) { setStatus('Whisper: ' + (j.error || 'fallo'), 'err'); return; }
    appendText(j.text || '');
    setStatus(session?.paused ? 'Pausado.' : 'Escuchando...');
  }

  function appendText(text) {
    if (!session || !text) { return; }
    const t = session.textarea;
    const trimmed = text.trim();
    if (!trimmed) { return; }
    // Append al final con espacio cuando ya hay contenido. (No tocamos el
    // caret porque el textarea puede no tener focus durante el dictado.)
    const cur = t.value || '';
    const sep = cur.length > 0 && !cur.endsWith(' ') && !cur.endsWith('\n') ? ' ' : '';
    t.value = cur + sep + trimmed;
    // Dispatch 'change' para que el binding de Blazor capture (autosave).
    t.dispatchEvent(new Event('change', { bubbles: true }));

    // Conteo de palabras para forzar un dispatch extra cada 10 (en caso de
    // que el autosave del FormViewer tenga un debounce largo).
    const newWords = trimmed.split(/\s+/).filter(Boolean).length;
    session.wordsSinceFlush += newWords;
    updateWordCount();
    if (session.wordsSinceFlush >= 10) {
      session.wordsSinceFlush = 0;
      t.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }

  async function stopSession() {
    if (!session) { return; }
    const s = session;
    session = null;
    if (s.timer) { clearInterval(s.timer); }
    try {
      if (s.recorder.state === 'recording') { s.recorder.stop(); }
    } catch (_) { /* ya cerrado */ }
    // Esperar un tick para que el ultimo chunk se procese.
    setTimeout(() => {
      try { s.stream.getTracks().forEach(tr => tr.stop()); } catch (_) {}
    }, 300);
    s.btn.classList.remove('recording');
    closePopover();
  }

  function togglePause() {
    if (!session) { return; }
    session.paused = !session.paused;
    if (session.paused) {
      try { if (session.recorder.state === 'recording') { session.recorder.stop(); } } catch (_) {}
      setStatus('Pausado.', 'paused');
      session.btn.classList.remove('recording');
    } else {
      maybeRestart();
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
  function updateWordCount() {
    if (!popover || !session) { return; }
    const c = document.getElementById('fv-voice-count');
    if (c) {
      const total = (session.textarea.value || '').split(/\s+/).filter(Boolean).length;
      c.textContent = total + ' palabras dictadas';
    }
  }

  // ===== Estilos del popover (inyectados una sola vez) =====
  function injectStyles() {
    if (document.getElementById('fv-voice-styles')) { return; }
    const s = document.createElement('style');
    s.id = 'fv-voice-styles';
    s.textContent = `
.fv-voice-pop {
  position: fixed; right: 24px; bottom: 24px; z-index: 9000;
  background: #ffffff; border: 1px solid #cbd5e1; border-radius: 12px;
  box-shadow: 0 12px 32px rgba(15,23,42,.25);
  width: 280px; padding: 14px; display: none; font-family: inherit;
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
