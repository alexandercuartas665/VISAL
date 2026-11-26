// Editor enriquecido (TinyMCE) para la descripcion/contenido de una leccion.
// TinyMCE esta AUTO-ALOJADO en wwwroot/lib/tinymce (GPL, v7.6.1) y se carga
// BAJO DEMANDA la primera vez que se abre el editor, asi no pesa en el resto de
// la app ni depende de ningun CDN externo.
// Las imagenes pegadas quedan embebidas como base64 (no se suben al servidor).
(function () {
    "use strict";
    var SRC = "/lib/tinymce/tinymce.min.js";
    var BASE = "/lib/tinymce";
    var loading = null;

    function ensure() {
        if (window.tinymce) { return Promise.resolve(); }
        if (loading) { return loading; }
        loading = new Promise(function (resolve, reject) {
            var sc = document.createElement("script");
            sc.src = SRC;
            sc.onload = function () { resolve(); };
            sc.onerror = function () { loading = null; reject(new Error("No se pudo cargar el editor.")); };
            document.head.appendChild(sc);
        });
        return loading;
    }

    function idOf(sel) { return sel && sel.charAt(0) === "#" ? sel.slice(1) : sel; }

    function leerDataUrl(blob) {
        return new Promise(function (resolve, reject) {
            var fr = new FileReader();
            fr.onload = function () { resolve(fr.result); };
            fr.onerror = function () { reject(new Error("No se pudo leer la imagen.")); };
            fr.readAsDataURL(blob);
        });
    }

    // Devuelve la imagen como data URL; si pesa mas de 400 KB o mide mas de 1600 px se
    // redibuja en un canvas (max 1600 px de lado) y se exporta como JPEG 0.85.
    function reducir(blob) {
        var MAX = 1600;
        return leerDataUrl(blob).then(function (url) {
            if (blob.type === "image/gif") { return url; }
            return new Promise(function (resolve) {
                var img = new Image();
                img.onload = function () {
                    var w = img.naturalWidth, h = img.naturalHeight;
                    if (blob.size <= 400 * 1024 && w <= MAX && h <= MAX) { resolve(url); return; }
                    var k = Math.min(1, MAX / Math.max(w, h));
                    var c = document.createElement("canvas");
                    c.width = Math.round(w * k); c.height = Math.round(h * k);
                    var g = c.getContext("2d");
                    g.fillStyle = "#fff"; g.fillRect(0, 0, c.width, c.height);
                    g.drawImage(img, 0, 0, c.width, c.height);
                    resolve(c.toDataURL("image/jpeg", 0.85));
                };
                img.onerror = function () { resolve(url); };
                img.src = url;
            });
        });
    }

    window.capEditor = {
        // Inicializa el editor sobre el textarea <selector> con el HTML inicial dado.
        init: function (selector, html) {
            return ensure().then(function () {
                try { window.tinymce.remove(selector); } catch (e) { /* no existia */ }
                return window.tinymce.init({
                    selector: selector,
                    license_key: "gpl",
                    base_url: BASE,
                    suffix: ".min",
                    height: 360,
                    menubar: false,
                    branding: false,
                    promotion: false,
                    plugins: "lists link table image code autoresize",
                    toolbar: "undo redo | bold italic underline strikethrough | bullist numlist | link table image | alignleft aligncenter alignright | removeformat code",
                    paste_data_images: true,
                    automatic_uploads: false,
                    content_style: "body{font-family:inherit;font-size:14px}"
                }).then(function () {
                    var ed = window.tinymce.get(idOf(selector));
                    if (ed) { ed.setContent(html || ""); }
                });
            });
        },
        // IMP-71: editor compacto para observaciones de autorizacion (texto enriquecido +
        // imagenes). Las imagenes (pegadas o elegidas del equipo) se embeben en base64 y, si
        // son grandes, se reducen a 1600 px / JPEG para no superar el tope de 2 MB.
        initObs: function (selector, html, placeholder) {
            return ensure().then(function () {
                try { window.tinymce.remove(selector); } catch (e) { /* no existia */ }
                return window.tinymce.init({
                    selector: selector,
                    license_key: "gpl",
                    base_url: BASE,
                    suffix: ".min",
                    height: 240,
                    min_height: 200,
                    max_height: 460,
                    menubar: false,
                    branding: false,
                    promotion: false,
                    statusbar: false,
                    placeholder: placeholder || "",
                    plugins: "lists link image autoresize",
                    toolbar: "bold italic underline strikethrough forecolor backcolor | bullist numlist | link image | removeformat",
                    paste_data_images: true,
                    automatic_uploads: true,
                    images_file_types: "jpg,jpeg,png,gif,webp",
                    image_dimensions: false,
                    images_upload_handler: function (blobInfo) { return reducir(blobInfo.blob()); },
                    file_picker_types: "image",
                    file_picker_callback: function (cb) {
                        var inp = document.createElement("input");
                        inp.type = "file";
                        inp.accept = "image/*";
                        inp.onchange = function () {
                            var f = inp.files && inp.files[0];
                            if (!f) { return; }
                            reducir(f).then(function (url) { cb(url, { alt: f.name }); });
                        };
                        inp.click();
                    },
                    content_style: "body{font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:14px;line-height:1.45} p{margin:0 0 6px} img{max-width:100%;height:auto}"
                }).then(function () {
                    var ed = window.tinymce.get(idOf(selector));
                    if (ed) { ed.setContent(html || ""); }
                });
            });
        },
        // Devuelve el HTML actual del editor.
        get: function (selector) {
            var ed = window.tinymce && window.tinymce.get(idOf(selector));
            return ed ? ed.getContent() : "";
        },
        // Destruye la instancia (al cerrar el modal).
        destroy: function (selector) {
            try { window.tinymce.remove(selector); } catch (e) { /* nada */ }
        }
    };
})();
