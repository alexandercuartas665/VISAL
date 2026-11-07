using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Visal.Application.Common;
using Visal.Infrastructure.Persistence;

namespace Visal.Application.Tests;

/// <summary>
/// Factory de pruebas para IApplicationDbContextFactory (Ola 2 estabilizacion). Entrega contextos
/// NUEVOS sobre el MISMO store in-memory del contexto fuente (reusa sus DbContextOptions), igual
/// que el factory de produccion (opciones compartidas, contexto nuevo por Create()). Asi los
/// metodos migrados a usar "using var db = factory.Create()" funcionan en tests compartiendo datos.
/// La resolucion de opciones es perezosa (en Create, no en el ctor) para no fallar al construir
/// servicios cuyos tests no ejercitan ningun metodo migrado.
/// </summary>
internal sealed class TestDbContextFactory(VisalDbContext source, ITenantContext tenant) : IApplicationDbContextFactory
{
    public IApplicationDbContext Create() =>
        new VisalDbContext((DbContextOptions<VisalDbContext>)source.GetService<IDbContextOptions>(), tenant);
}
