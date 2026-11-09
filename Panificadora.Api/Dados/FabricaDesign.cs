using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Panificadora.Api.Dados;

public sealed class FabricaDesign : IDesignTimeDbContextFactory<ComercialDb>
{
    public ComercialDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ComercialDb>()
        .UseMySQL(Environment.GetEnvironmentVariable("ConnectionStrings__Distribuidora")
            ?? "Server=localhost;Database=panificadora;User=design")
        .Options);
}
