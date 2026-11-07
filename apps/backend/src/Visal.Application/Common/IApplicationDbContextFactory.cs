namespace Visal.Application.Common;

/// <summary>
/// Crea un <see cref="IApplicationDbContext"/> NUEVO e independiente por cada operacion.
///
/// Motivo (Ola 2 de estabilizacion, P0): en Blazor Server el DbContext scoped vive todo el
/// circuito y es compartido por los servicios; dos operaciones que se solapan (render + evento,
/// cargas rapidas, navegacion con consulta en vuelo) chocan con
/// "A second operation was started on this context instance...". La cura de fondo es usar un
/// contexto CORTO por operacion: cada metodo hace <c>using var db = _factory.Create();</c> y
/// trabaja sobre ese contexto, que nadie mas comparte.
///
/// El contexto creado hereda el tenant del circuito (ITenantContext scoped), asi que los query
/// filters por TenantId siguen aplicando igual. El llamador es el DUENO del contexto y DEBE
/// desecharlo (por eso <see cref="IApplicationDbContext"/> es IDisposable/IAsyncDisposable).
/// Dentro de una misma operacion que use helpers, se pasa el MISMO contexto por parametro para
/// conservar la consistencia (no crear uno por helper).
/// </summary>
/// <remarks>
/// CONVENCION (Ola 3 de estabilizacion, P0): los servicios NUEVOS que se consuman desde el
/// circuito Blazor (paginas/componentes .razor interactivos) NO deben inyectar el
/// <see cref="IApplicationDbContext"/> scoped como campo del ctor. Deben inyectar esta factory y
/// abrir <c>using var db = factory.Create();</c> por operacion. Inyectar el contexto scoped
/// reintroduce el P0 (segunda operacion sobre el mismo contexto). La unica excepcion temporal es
/// la cola de servicios aun sin migrar (tail progresivo) y las escrituras de HistoriaClinica
/// (pendientes de Ola 4, que migra su contexto junto con la transaccion de atomicidad).
/// Un analizador Roslyn que lo imponga queda diferido hasta terminar de migrar el tail.
/// </remarks>
public interface IApplicationDbContextFactory
{
    IApplicationDbContext Create();
}
