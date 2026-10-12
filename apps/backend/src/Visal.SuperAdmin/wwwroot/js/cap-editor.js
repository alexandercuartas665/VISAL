// Editor enriquecido (TinyMCE) para la descripcion/contenido de una leccion.
// TinyMCE se carga BAJO DEMANDA desde jsDelivr la primera vez que se abre el
// editor, asi no pesa en el resto de la app. Licencia GPL (sin API key / nube).
// Las imagenes pegadas quedan embebidas como base64 (no se suben al servidor).
(function () {
    "use strict";
    var VER = "7.6.1";
    var SRC = "https://cdn.jsdelivr.net/npm/tinymce@" + VER + "/tinymce.min.js";
    var BASE = "https://cdn.jsdelivr.net/npm/tinymce@" + VER;
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
