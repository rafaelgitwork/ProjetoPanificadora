using Microsoft.EntityFrameworkCore;
using Panificadora.Contratos;

namespace Panificadora.Api.Dados;

public sealed record Credencial(Guid Id, string SenhaHash);

public sealed class ComercialDb(DbContextOptions<ComercialDb> options) : DbContext(options)
{
    public DbSet<Estado> Estados => Set<Estado>();
    public DbSet<Cidade> Cidades => Set<Cidade>();
    public DbSet<Grupo> Grupos => Set<Grupo>();
    public DbSet<Forma> Formas => Set<Forma>();
    public DbSet<Vendedor> Vendedores => Set<Vendedor>();
    public DbSet<Credencial> Credenciais => Set<Credencial>();
    public DbSet<Territorio> Territorios => Set<Territorio>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Visita> Visitas => Set<Visita>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<Condicao> Condicoes => Set<Condicao>();
    public DbSet<Prazo> Prazos => Set<Prazo>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<LinhaPedido> Itens => Set<LinhaPedido>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<LinhaVenda> ItensVenda => Set<LinhaVenda>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<Titulo> Titulos => Set<Titulo>();
    public DbSet<Baixa> Baixas => Set<Baixa>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<string>().HaveMaxLength(250);
        builder.Properties<decimal>().HavePrecision(18, 4);
        builder.Properties<DateOnly>().HaveConversion<DataCivil>().HaveColumnType("date");
        builder.Properties<DateTimeOffset>().HaveConversion<InstanteUtc>().HaveColumnType("datetime(6)");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Vendedor>().Ignore(v => v.Senha).HasIndex(v => v.Email).IsUnique();
        model.Entity<Credencial>().Property(v => v.SenhaHash).HasMaxLength(512);
        model.Entity<Estado>().HasIndex(e => e.Uf).IsUnique();
        model.Entity<Cidade>().HasIndex(c => new { c.EstadoId, c.Nome }).IsUnique();
        model.Entity<Territorio>().HasIndex(t => new { t.CidadeId, t.Nome }).IsUnique();
        model.Entity<Cliente>().HasIndex(c => c.Documento).IsUnique();
        model.Entity<Visita>().HasIndex(v => new { v.ClienteId, v.Dia }).IsUnique();
        model.Entity<Venda>().HasIndex(v => v.PedidoId).IsUnique();
        model.Entity<Prazo>().HasIndex(p => new { p.CondicaoId, p.Numero }).IsUnique();
        model.Entity<Titulo>().HasIndex(t => new { t.VendaId, t.Numero }).IsUnique();

        Relacao<Cidade, Estado>(nameof(Cidade.EstadoId));
        Relacao<Territorio, Cidade>(nameof(Territorio.CidadeId));
        Relacao<Territorio, Vendedor>(nameof(Territorio.VendedorId));
        Relacao<Credencial, Vendedor>(nameof(Credencial.Id));
        Relacao<Cliente, Territorio>(nameof(Cliente.TerritorioId));
        Relacao<Visita, Cliente>(nameof(Visita.ClienteId));
        Relacao<Produto, Grupo>(nameof(Produto.GrupoId));
        Relacao<Lote, Produto>(nameof(Lote.ProdutoId));
        Relacao<Prazo, Condicao>(nameof(Prazo.CondicaoId));
        Relacao<Prazo, Forma>(nameof(Prazo.FormaId));
        Relacao<Pedido, Cliente>(nameof(Pedido.ClienteId));
        Relacao<Pedido, Vendedor>(nameof(Pedido.VendedorId));
        Relacao<Pedido, Condicao>(nameof(Pedido.CondicaoId));
        Relacao<LinhaPedido, Pedido>(nameof(LinhaPedido.PedidoId));
        Relacao<LinhaPedido, Produto>(nameof(LinhaPedido.ProdutoId));
        Relacao<Venda, Pedido>(nameof(Venda.PedidoId));
        Relacao<Venda, Cliente>(nameof(Venda.ClienteId));
        Relacao<LinhaVenda, Venda>(nameof(LinhaVenda.VendaId));
        Relacao<LinhaVenda, Produto>(nameof(LinhaVenda.ProdutoId));
        Relacao<Reserva, Venda>(nameof(Reserva.VendaId));
        Relacao<Reserva, Lote>(nameof(Reserva.LoteId));
        Relacao<Titulo, Venda>(nameof(Titulo.VendaId));
        Relacao<Baixa, Titulo>(nameof(Baixa.TituloId));

        void Relacao<TDependente, TPrincipal>(string chave)
            where TDependente : class where TPrincipal : class =>
            model.Entity<TDependente>().HasOne<TPrincipal>().WithMany().HasForeignKey(chave)
                .OnDelete(DeleteBehavior.Restrict);
    }

    public async Task<T> Salvar<T>(Guid id, T registro) where T : class
    {
        var anterior = await Set<T>().FindAsync(id);
        if (anterior is null) Add(registro);
        else Entry(anterior).CurrentValues.SetValues(registro);
        return registro;
    }
}

public sealed class DataCivil() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateOnly, DateTime>(
    data => data.ToDateTime(TimeOnly.MinValue), instante => DateOnly.FromDateTime(instante));

public sealed class InstanteUtc() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTime>(
    instante => instante.UtcDateTime, data => new DateTimeOffset(DateTime.SpecifyKind(data, DateTimeKind.Utc)));
