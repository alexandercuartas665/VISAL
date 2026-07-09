using Visal.Application.Tenancy;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Estado compartido entre HistoriasClinicasModulo (padre) y sus sub-tabs
/// (HcDocumentos, HcEscalas) para orquestar la seleccion de cual contacto de
/// emergencia del paciente firma un documento cuando el paciente tiene varios.
///
/// Se instancia en el padre y se expone via CascadingValue. Los sub-tabs lo
/// consumen como CascadingParameter y llaman ResolverAsync antes de aplicar el
/// prefill de firmaAcompanante* — la clase encapsula el estado (para no
/// re-preguntar en cada documento del mismo paciente) y la comunicacion con
/// el modal (para pedir input al usuario cuando hay ambiguedad).
///
/// Semantica de OrdenActivo:
///   null  = no se ha resuelto todavia -> ResolverAsync debe correr el flujo
///   0     = "sin acompanante / firma el paciente" -> no aplicar prefill
///   1..N  = Orden del contacto elegido -> aplicar ese contacto al slot 1 del
///           schema del formulario (los slots 2, 3, 4 mantienen el comportamiento
///           historico de N-esimo por Orden).
/// </summary>
public class AcompananteSelectorContext
{
    private TaskCompletionSource<int?>? _tcs;

    /// <summary>Se dispara cuando el estado cambia. El padre debe llamar
    /// StateHasChanged para refrescar el chip y el modal.</summary>
    public event Action? StateChanged;

    /// <summary>Orden del contacto elegido, 0 para "sin acompanante", o null si
    /// aun no se resolvio para este paciente.</summary>
    public int? OrdenActivo { get; private set; }

    /// <summary>True cuando el modal SelectorAcompananteModal debe estar visible.
    /// El padre suele bindarlo directo al IsOpen del componente modal.</summary>
    public bool ModalVisible { get; private set; }

    /// <summary>Lista de contactos que el modal presentara al usuario. Solo tiene
    /// valor mientras ModalVisible = true. Antes de ese momento esta vacia.</summary>
    public IReadOnlyList<PacienteContactoEmergenciaDto> Contactos { get; private set; }
        = Array.Empty<PacienteContactoEmergenciaDto>();

    /// <summary>Nombre + parentesco del contacto activo, para el chip del header.
    /// Null si OrdenActivo no esta fijo o es 0 (sin acompanante).</summary>
    public string? EtiquetaActivo { get; private set; }

    /// <summary>
    /// Resuelve cual contacto usar. Con 0 contactos, devuelve 0 directo
    /// (no hay acompanante). Con 1, fija ese Orden y lo devuelve. Con 2+,
    /// abre el modal y espera la eleccion del usuario. Si ya hay OrdenActivo
    /// fijo, lo devuelve sin volver a preguntar (memoria por paciente).
    ///
    /// Devuelve el Orden elegido, 0 para "sin acompanante", o null si el
    /// usuario cancelo el modal — en ese caso el caller debe abortar el
    /// flujo de crear documento.
    /// </summary>
    public Task<int?> ResolverAsync(IReadOnlyList<PacienteContactoEmergenciaDto> contactos)
    {
        // Ya se resolvio antes en esta sesion del paciente: usar lo memorizado.
        if (OrdenActivo.HasValue) { return Task.FromResult<int?>(OrdenActivo); }

        // 0 contactos: no hay nada que elegir. Marcamos 0 = sin acompanante
        // pero NO fijamos EtiquetaActivo (no hay a quien nombrar).
        if (contactos.Count == 0)
        {
            OrdenActivo = 0;
            EtiquetaActivo = null;
            StateChanged?.Invoke();
            return Task.FromResult<int?>(0);
        }

        // 1 contacto: no hay ambiguedad. Fijamos ese Orden.
        if (contactos.Count == 1)
        {
            var only = contactos.OrderBy(x => x.Orden).ThenBy(x => x.Nombre).First();
            OrdenActivo = only.Orden;
            EtiquetaActivo = Etiqueta(only);
            StateChanged?.Invoke();
            return Task.FromResult<int?>(OrdenActivo);
        }

        // 2+ contactos: mostrar modal y esperar respuesta.
        Contactos = contactos;
        ModalVisible = true;
        _tcs = new TaskCompletionSource<int?>();
        StateChanged?.Invoke();
        return _tcs.Task;
    }

    /// <summary>Se llama desde el modal cuando el usuario elige un contacto (o 0
    /// para "sin acompanante"). Cierra el modal, fija el estado y libera el
    /// caller que estaba esperando en ResolverAsync.</summary>
    public void Elegir(int orden)
    {
        OrdenActivo = orden;
        if (orden > 0)
        {
            var c = Contactos.FirstOrDefault(x => x.Orden == orden);
            EtiquetaActivo = c is null ? null : Etiqueta(c);
        }
        else
        {
            EtiquetaActivo = null;
        }
        ModalVisible = false;
        var pending = _tcs; _tcs = null;
        StateChanged?.Invoke();
        pending?.TrySetResult(orden);
    }

    /// <summary>Se llama desde el modal cuando el usuario cierra sin elegir. El
    /// caller que espera ResolverAsync recibe null y debe abortar el flujo.</summary>
    public void Cancelar()
    {
        ModalVisible = false;
        var pending = _tcs; _tcs = null;
        StateChanged?.Invoke();
        pending?.TrySetResult(null);
    }

    /// <summary>Limpia el estado. Se llama al cambiar de paciente o cuando el
    /// usuario pulsa "Cambiar acompanante" en el chip del header.</summary>
    public void Reset()
    {
        OrdenActivo = null;
        EtiquetaActivo = null;
        Contactos = Array.Empty<PacienteContactoEmergenciaDto>();
        // Si habia un modal abierto lo cerramos y devolvemos null al caller.
        var pending = _tcs; _tcs = null; ModalVisible = false;
        StateChanged?.Invoke();
        pending?.TrySetResult(null);
    }

    private static string Etiqueta(PacienteContactoEmergenciaDto c)
    {
        var nombre = c.Nombre?.Trim() ?? "(sin nombre)";
        return string.IsNullOrWhiteSpace(c.Parentesco) ? nombre : $"{nombre} ({c.Parentesco})";
    }
}
