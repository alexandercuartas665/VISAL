// =========================================================================
//  nm-firma.js
//  Pad de firma sobre un <canvas>. Soporta mouse y touch (dedo/lapiz). El
//  trazo es absoluto al canvas (no al viewport) y respeta DPR para que se
//  vea nitido en moviles. Se publica como window.visalFirma para que el
//  componente Blazor lo invoque via IJSRuntime.
// =========================================================================
(function () {
    const pads = new Map(); // canvasId -> { ctx, drawing, last }

    function getPos(canvas, ev) {
        const rect = canvas.getBoundingClientRect();
        const point = ev.touches && ev.touches[0] ? ev.touches[0] : ev;
        return {
            x: (point.clientX - rect.left) * (canvas.width / rect.width),
            y: (point.clientY - rect.top) * (canvas.height / rect.height)
        };
    }

    function setup(canvas) {
        // Resolution: ajustamos width/height segun DPR para nitidez en moviles.
        const dpr = window.devicePixelRatio || 1;
        const cssW = canvas.clientWidth;
        const cssH = canvas.clientHeight || 175;
        canvas.width = Math.floor(cssW * dpr);
        canvas.height = Math.floor(cssH * dpr);
        const ctx = canvas.getContext('2d');
        ctx.lineWidth = 2.2 * dpr;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.strokeStyle = '#0f172a';
        // Fondo blanco (para guardar como png con fondo, no transparente).
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        return ctx;
    }

    window.visalFirma = {
        init: function (canvasId) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) { return false; }
            // OJO: Blazor puede destruir y recrear el <canvas> con el mismo id
            // cuando el usuario alterna "Cambiar firma". Si nos quedamos con el
            // pad viejo, los listeners apuntan a un nodo que ya no esta en el
            // DOM y el nuevo canvas no responde al mouse/touch. Si es el mismo
            // nodo, no re-cablear (evita listeners duplicados).
            const prev = pads.get(canvasId);
            if (prev && prev.canvas === canvas) { return true; }
            if (prev) { pads.delete(canvasId); }
            const ctx = setup(canvas);
            const state = { canvas, ctx, drawing: false, last: null };
            pads.set(canvasId, state);

            const onStart = (ev) => {
                ev.preventDefault();
                state.drawing = true;
                state.last = getPos(canvas, ev);
            };
            const onMove = (ev) => {
                if (!state.drawing) { return; }
                ev.preventDefault();
                const p = getPos(canvas, ev);
                ctx.beginPath();
                ctx.moveTo(state.last.x, state.last.y);
                ctx.lineTo(p.x, p.y);
                ctx.stroke();
                state.last = p;
            };
            const onEnd = () => { state.drawing = false; state.last = null; };

            canvas.addEventListener('mousedown', onStart);
            canvas.addEventListener('mousemove', onMove);
            canvas.addEventListener('mouseup', onEnd);
            canvas.addEventListener('mouseleave', onEnd);
            canvas.addEventListener('touchstart', onStart, { passive: false });
            canvas.addEventListener('touchmove', onMove, { passive: false });
            canvas.addEventListener('touchend', onEnd);
            canvas.addEventListener('touchcancel', onEnd);
            return true;
        },

        clear: function (canvasId) {
            const canvas = document.getElementById(canvasId);
            const state = pads.get(canvasId);
            if (!canvas || !state) { return false; }
            state.ctx.fillStyle = '#ffffff';
            state.ctx.fillRect(0, 0, canvas.width, canvas.height);
            return true;
        },

        // Devuelve PNG data URL. Si el canvas esta vacio devuelve null
        // (heuristica: si toda la imagen es blanca exacta, no hay firma).
        getDataUrl: function (canvasId) {
            const canvas = document.getElementById(canvasId);
            const state = pads.get(canvasId);
            if (!canvas || !state) { return null; }
            // Verificar si esta vacio - sample de 200 pixeles.
            const data = state.ctx.getImageData(0, 0, canvas.width, canvas.height).data;
            let allWhite = true;
            for (let i = 0; i < data.length; i += 4 * 50) {
                if (data[i] < 250 || data[i + 1] < 250 || data[i + 2] < 250) {
                    allWhite = false; break;
                }
            }
            if (allWhite) { return null; }
            return canvas.toDataURL('image/png');
        },

        // Pinta una firma existente (data url) sobre el canvas.
        load: function (canvasId, dataUrl) {
            const canvas = document.getElementById(canvasId);
            const state = pads.get(canvasId);
            if (!canvas || !state || !dataUrl) { return false; }
            const img = new Image();
            img.onload = function () {
                state.ctx.fillStyle = '#ffffff';
                state.ctx.fillRect(0, 0, canvas.width, canvas.height);
                state.ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
            };
            img.src = dataUrl;
            return true;
        }
    };
})();
