using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class MessageTemplateService : IMessageTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public MessageTemplateService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<MessageTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.MessageTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Category).ThenBy(t => t.SortOrder)
            .Select(t => new MessageTemplateDto(t.Id, t.Category, t.Body, t.MediaType, t.MediaUrl, t.MediaMimeType, t.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<MessageTemplateDto?> CreateAsync(CreateMessageTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }
        var category = (request.Category ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(category))
        {
            category = "saludo";
        }
        var nextOrder = await _db.MessageTemplates
            .Where(t => t.Category == category)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var entity = new MessageTemplate
        {
            TenantId = tenantId,
            Category = category,
            Body = (request.Body ?? "").Trim(),
            MediaType = request.MediaType,
            MediaUrl = request.MediaUrl,
            MediaMimeType = request.MediaMimeType,
            SortOrder = nextOrder + 1,
            IsActive = true
        };
        _db.MessageTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<MessageTemplateDto?> UpdateAsync(Guid id, UpdateMessageTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }
        entity.Category = (request.Category ?? entity.Category).Trim().ToLowerInvariant();
        entity.Body = (request.Body ?? "").Trim();
        entity.MediaType = request.MediaType;
        entity.MediaUrl = request.MediaUrl;
        entity.MediaMimeType = request.MediaMimeType;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }
        _db.MessageTemplates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return 0;
        }
        var exists = await _db.MessageTemplates.AnyAsync(cancellationToken);
        if (exists)
        {
            return 0;
        }

        var created = 0;
        foreach (var (category, items) in _defaults)
        {
            var order = 0;
            foreach (var body in items)
            {
                _db.MessageTemplates.Add(new MessageTemplate
                {
                    TenantId = tenantId,
                    Category = category,
                    Body = body,
                    MediaType = MessageMediaType.None,
                    SortOrder = order++,
                    IsActive = true
                });
                created++;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static MessageTemplateDto Map(MessageTemplate t) =>
        new(t.Id, t.Category, t.Body, t.MediaType, t.MediaUrl, t.MediaMimeType, t.SortOrder);

    // Mensajes del prototipo (IPS clinica). Los tokens {paciente}, {cedula}, {fecha} se
    // rellenan en el cliente al pulsar el chip (con datos del PacienteActivo).
    private static readonly (string Category, string[] Items)[] _defaults =
    {
        ("saludo", new[]
        {
            "Hola {paciente}! Le escribo de IPS Visal para coordinar su atencion domiciliaria.",
            "Buen dia {paciente}, soy de la IPS Visal. Le escribo para confirmar la atencion de hoy.",
            "Hola, le saluda IPS Visal RT. Como se encuentra hoy {paciente}?"
        }),
        ("recordatorio", new[]
        {
            "Le recordamos su cita / visita domiciliaria programada para el {fecha}. Por favor confirme su disponibilidad.",
            "{paciente}, su profesional pasa hoy por su domicilio. Por favor tenga lista la documentacion (CC {cedula}).",
            "Recuerde tener listos los medicamentos y la historia clinica para la visita de hoy."
        }),
        ("info", new[]
        {
            "Para programar su atencion necesitamos confirmar su direccion actual y un telefono adicional. Nos puede ayudar?",
            "{paciente}, podria indicarnos si presenta alguna novedad o sintoma nuevo desde la ultima visita?",
            "Necesitamos la autorizacion vigente de la EPS. La puede compartir por este mismo medio?",
            "Tiene algun medicamento que se le este por agotar? Asi lo coordinamos con anticipacion."
        }),
        ("seguimiento", new[]
        {
            "Hola {paciente}, queriamos saber como se encuentra despues de la ultima visita.",
            "Le recordamos que el profesional pasa por su domicilio segun lo programado. Cualquier novedad nos avisa.",
            "Estamos pendientes de su evolucion. Si necesita reagendar la atencion por favor nos lo comunica.",
            "Por aqui me cuenta si tuvo algun inconveniente o si todo esta bien."
        }),
        ("alta", new[]
        {
            "{paciente}, su profesional le da de alta del servicio actual. Gracias por la confianza en IPS Visal RT.",
            "Se ha cerrado el ciclo de atencion. Para una nueva orden de servicio por favor contactarnos con la nueva autorizacion.",
            "Gracias por permitirnos cuidar de su salud. Cualquier emergencia comuniquese con nuestra linea de atencion."
        })
    };
}
