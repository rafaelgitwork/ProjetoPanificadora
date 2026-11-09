using Panificadora.Api.Comercial;
using Panificadora.Contratos;

namespace Panificadora.UnitTests;

public sealed class FinanceiroTests
{
    [Fact]
    public void Divide235EmDuasParcelasIguais() =>
        Assert.Equal([117.50m, 117.50m], Regras.Parcelar(235, [50, 50]));

    [Fact]
    public void ConservaCentavosComPercentuaisDistintos()
    {
        var partes = Regras.Parcelar(235, [33.33m, 33.33m, 33.34m]);
        Assert.Equal(235m, partes.Sum());
        Assert.Equal([78.33m, 78.32m, 78.35m], partes);
    }

    [Fact]
    public void RecusaPercentuaisQueNaoSomamCem() =>
        Assert.Throws<RegraException>(() => Regras.Parcelar(235, [50, 49]));

    [Fact]
    public void AplicaDescontoNaQuitacaoAntecipada()
    {
        var vencimento = new DateOnly(2026, 11, 2);
        var titulo = new Titulo(Guid.NewGuid(), Guid.NewGuid(), 1, "Pix", vencimento, 100, 0, 5, 2, 1);
        Assert.Equal((0m, 7m, 100m), Regras.Baixar(titulo, [], vencimento.AddDays(-7), 93));
    }

    [Fact]
    public void ConsideraReducaoDoPrincipalAposBaixaParcial()
    {
        var vencimento = new DateOnly(2026, 11, 2);
        var titulo = new Titulo(Guid.NewGuid(), Guid.NewGuid(), 1, "Boleto", vencimento, 100, 0, 5, 2, 1);
        var primeira = Regras.Baixar(titulo, [], vencimento.AddDays(2), 59);
        Assert.Equal((9m, 0m, 50m), primeira);
        var historico = new Baixa(Guid.NewGuid(), titulo.Id, vencimento.AddDays(2), 59, 50, 9, 0);
        Assert.Equal((1m, 0m, 50m), Regras.Baixar(titulo with { PrincipalPago = 50 }, [historico], vencimento.AddDays(3), 51));
    }
}
