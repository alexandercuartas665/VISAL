// =========================================================================
//  visal-image.js
//  Compresion de imagenes en el cliente para campos fieldType="image" del
//  Motor de Formularios. Lee el archivo de un <input type=file>, lo escala al
//  lado mayor indicado y lo exporta como JPEG con la calidad dada, devolviendo
//  un data URL. La compresion ocurre ANTES de viajar al servidor (SignalR), asi
//  solo se transfiere la imagen ya reducida. Se publica como window.visalImage
//  para que el FormViewer lo invoque via IJSRuntime.
// =========================================================================
(function () {
    // Carga el archivo como bitmap orientado. createImageBitmap con
    // imageOrientation:'from-image' respeta el EXIF (fotos de celular salen
    // derechas). Fallback a <img> si el navegador no lo soporta.
    function loadBitmap(file) {
        if (window.createImageBitmap) {
            try {
                return createImageBitmap(file, { imageOrientation: 'from-image' });
            } catch (e) { /* cae al fallback */ }
        }
        return new Promise(function (resolve, reject) {
            var img = new Image();
            var url = URL.createObjectURL(file);
            img.onload = function () { URL.revokeObjectURL(url); resolve(img); };
            img.onerror = function (e) { URL.revokeObjectURL(url); reject(e); };
            img.src = url;
        });
    }

    window.visalImage = {
        // Comprime el archivo del input <inputId>. maxDim = lado mayor maximo en
        // px; quality = 0..1 para JPEG. Devuelve un data URL JPEG, o null si no
        // hay archivo o no es imagen.
        compress: async function (inputId, maxDim, quality) {
            var input = document.getElementById(inputId);
            if (!input || !input.files || input.files.length === 0) { return null; }
            var file = input.files[0];
            if (!file.type || file.type.indexOf('image/') !== 0) { return null; }
            maxDim = maxDim || 1600;
            quality = (quality && quality > 0 && quality <= 1) ? quality : 0.72;

            var bmp;
            try { bmp = await loadBitmap(file); }
            catch (e) { return null; }

            var w0 = bmp.width, h0 = bmp.height;
            if (!w0 || !h0) { return null; }

            var scale = Math.min(1, maxDim / Math.max(w0, h0));
            var w = Math.max(1, Math.round(w0 * scale));
            var h = Math.max(1, Math.round(h0 * scale));

            var canvas = document.createElement('canvas');
            canvas.width = w;
            canvas.height = h;
            var ctx = canvas.getContext('2d');
            // Fondo blanco: JPEG no tiene canal alpha; evita que zonas
            // transparentes (PNG) queden en negro.
            ctx.fillStyle = '#ffffff';
            ctx.fillRect(0, 0, w, h);
            ctx.drawImage(bmp, 0, 0, w, h);
            if (bmp.close) { try { bmp.close(); } catch (e) { } }

            var dataUrl;
            try { dataUrl = canvas.toDataURL('image/jpeg', quality); }
            catch (e) { return null; }

            // Limpia el input para permitir re-seleccionar el mismo archivo.
            try { input.value = ''; } catch (e) { }
            return dataUrl;
        }
    };
})();
