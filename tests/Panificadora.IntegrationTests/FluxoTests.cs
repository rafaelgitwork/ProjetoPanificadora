using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySql.Data.MySqlClient;
using Panificadora.Contratos;

namespace Panificadora.IntegrationTests;

public sealed class FluxoTests
{
    [Fact]
    public async Task CarteiraCorteFaturamentoEParcelas()
    {
        var servidor = Environment.GetEnvironmentVariable("PANIFICADORA_TEST_MYSQL")
            ?? throw new InvalidOperationException("Informe PANIFICADORA_TEST_MYSQL com a conexão de um MySQL de teste.");
        var chave = new MySqlConnectionStringBuilder(servidor) { Database = "panificadora_" + Guid.NewGuid().ToString("N") };
        using var servidorMySql = new MySqlConnection(servidor);
        await servidorMySql.OpenAsync();
        await using var criar = new MySqlCommand($"CREATE DATABASE `{chave.Database}`", servidorMySql);
        await criar.ExecuteNonQueryAsync();
        try
        {
            await Executar(chave.ConnectionString);
        }
        finally
        {
            await using var remover = new MySqlCommand($"DROP DATABASE `{chave.Database}`", servidorMySql);
            await remover.ExecuteNonQueryAsync();
        }
    }

    private static async Task Executar(string conexao)
    {
        const string emailGestao = "gestao@exemplo.test";
        var senhaGestao = Guid.NewGuid().ToString("N");
        var senhaVendedor = Guid.NewGuid().ToString("N");
        var chaveJwt = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("ConnectionStrings:Distribuidora", conexao);
            web.UseSetting("Autenticacao:Chave", chaveJwt);
            web.UseSetting("Gestao:Email", emailGestao);
            web.UseSetting("Gestao:Senha", senhaGestao);
            web.UseSetting("Banco:Migrar", "true");
            web.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new Relogio());
            });
        });
        using var http = app.CreateClient();
        var gestao = new ClienteApi(http);
        gestao.Token = (await gestao.Postar<Sessao>("sessao", new Acesso(emailGestao, senhaGestao))).Token;
        Task<T> Cadastro<T>(string rota, T entrada) => gestao.Postar<T>("gestao/" + rota, entrada!);
        var estado = await Cadastro("estados", new Estado(Guid.NewGuid(), "PR", "Paraná"));
        var cidade = await Cadastro("cidades", new Cidade(Guid.NewGuid(), estado.Id, "Foz do Iguaçu"));
        var grupo = await Cadastro("grupos", new Grupo(Guid.NewGuid(), "Farinhas"));
        var vendedor = await Cadastro("vendedores", new Vendedor(Guid.NewGuid(), "Henrique", "vendas@exemplo.test", true)
            { Senha = senhaVendedor });
        Assert.Null(vendedor.Senha);
        var territorio = await Cadastro("territorios", new Territorio(Guid.NewGuid(), cidade.Id, "Centro", vendedor.Id));
        var outro = await Cadastro("vendedores", new Vendedor(Guid.NewGuid(), "Joana", "joana@exemplo.test", true)
            { Senha = senhaVendedor });
        var territorioAlheio = await Cadastro("territorios", new Territorio(Guid.NewGuid(), cidade.Id, "Vila C", outro.Id));
        var campo = new ClienteApi(http);
        campo.Token = (await campo.Postar<Sessao>("sessao", new Acesso(vendedor.Email, senhaVendedor))).Token;
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<HttpRequestException>(() => campo.Obter<Painel>("gestao"))).StatusCode);
        var novo = new Cliente(Guid.NewGuid(), territorio.Id, "Padaria A", "00000000000011",
            "a@exemplo.test", "45999999999", "Avenida Paraná", true);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<HttpRequestException>(
            () => campo.Postar<Cliente>("campo/clientes", novo with { TerritorioId = territorioAlheio.Id }))).StatusCode);
        var a = await campo.Postar<Cliente>("campo/clientes", novo);
        Assert.Equal(a, await campo.Postar<Cliente>("campo/clientes", novo));
        var b = await Cadastro("clientes", novo with { Id = Guid.NewGuid(), Nome = "Padaria B", Documento = "00000000000022" });
        var c = await Cadastro("clientes", novo with { Id = Guid.NewGuid(), Nome = "Padaria C", Documento = "00000000000033" });
        await Cadastro("clientes", novo with { Id = Guid.NewGuid(), TerritorioId = territorioAlheio.Id, Documento = "00000000000044" });
        foreach (var (cliente, posicao) in new[] { (a, 10), (b, 20), (c, 15) })
            await Cadastro("visitas", new Visita(Guid.NewGuid(), cliente.Id, DayOfWeek.Monday, posicao));
        var carteira = await campo.Obter<DadosVendedor>("campo/dados");
        Assert.Equal(3, carteira.Clientes.Length);
        Assert.Equal([10, 15, 20], carteira.Visitas.Select(v => v.Sequencia));

        var hoje = new DateOnly(2026, 10, 19);
        var produto = await Cadastro("produtos", new Produto(Guid.NewGuid(), grupo.Id, "Farinha", "Saco", "Local seco",
            117.50m, 1, hoje, true));
        var primeiro = await Cadastro("lotes", new Lote(Guid.NewGuid(), produto.Id, hoje.AddDays(2), 5));
        await Cadastro("lotes", new Lote(Guid.NewGuid(), produto.Id, hoje.AddDays(5), 10));
        var pix = await Cadastro("formas", new Forma(Guid.NewGuid(), "Pix"));
        var boleto = await Cadastro("formas", new Forma(Guid.NewGuid(), "Boleto"));
        var condicao = new Condicao(Guid.NewGuid(), "7/14 dias", 5, 2, 1);
        await gestao.Postar<Condicao>("gestao/condicoes", new CadastroCondicao(condicao,
            [new(Guid.NewGuid(), condicao.Id, 1, 7, 50, pix.Id), new(Guid.NewGuid(), condicao.Id, 2, 14, 50, boleto.Id)]));

        foreach (var (cliente, quantidade) in new[] { (a, 5), (b, 8), (c, 9) })
        {
            var pedido = new NovoPedido(Guid.NewGuid(), cliente.Id, condicao.Id, [new(produto.Id, quantidade, produto.Preco)]);
            var recebido = await campo.Postar<Pedido>("campo/pedidos", pedido);
            Assert.Equal(recebido, await campo.Postar<Pedido>("campo/pedidos", pedido));
        }

        var painel = await gestao.Obter<Painel>("gestao");
        Assert.Equal(3, painel.Pedidos.Length);
        var ordem = new[] { (a, 2), (b, 6), (c, 7) };
        foreach (var (cliente, quantidade) in ordem)
        {
            var pedido = painel.Pedidos.Single(p => p.ClienteId == cliente.Id);
            var linha = painel.Itens.Single(i => i.PedidoId == pedido.Id);
            var rota = $"gestao/pedidos/{pedido.Id}/corte";
            if (cliente == c)
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<HttpRequestException>(
                    () => gestao.Postar<Pedido>(rota, new Corte([new(linha.Id, 8)])))).StatusCode);
                var aposRecusa = await gestao.Obter<Painel>("gestao");
                Assert.Null(aposRecusa.Itens.Single(i => i.Id == linha.Id).Liberado);
                Assert.Equal(8, aposRecusa.Lotes.Sum(l => l.Reservado));
            }
            await gestao.Postar<Pedido>(rota, new Corte([new(linha.Id, quantidade)]));
        }
        painel = await gestao.Obter<Painel>("gestao");
        Assert.Equal([2, 6, 7], ordem.Select(x => painel.Itens.Single(i =>
            i.PedidoId == painel.Pedidos.Single(p => p.ClienteId == x.Item1.Id).Id).Liberado!.Value));
        Assert.Equal(15, painel.Lotes.Sum(l => l.Reservado));

        var venda = painel.Vendas.Single(v => v.ClienteId == a.Id);
        Assert.Equal(235m, venda.Total);
        await gestao.Postar<Venda>($"gestao/vendas/{venda.Id}/nota", new Faturamento("123"));
        var saida = await gestao.Postar<Venda>($"gestao/vendas/{venda.Id}/expedicao", new { });
        Assert.Equal("Em rota", saida.Situacao);
        Assert.Equal(saida, await gestao.Postar<Venda>($"gestao/vendas/{venda.Id}/expedicao", new { }));
        painel = await gestao.Obter<Painel>("gestao");
        Assert.Equal(3, painel.Lotes.Single(l => l.Id == primeiro.Id).Fisico);
        var titulos = painel.Titulos.Where(t => t.VendaId == venda.Id).OrderBy(t => t.Numero).ToArray();
        Assert.Equal([117.50m, 117.50m], titulos.Select(t => t.Valor));
        Assert.Equal([new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 2)], titulos.Select(t => t.Vencimento));
        Assert.Equal(["Pix", "Boleto"], titulos.Select(t => t.Forma));
        var pagamento = new Pagamento(Guid.NewGuid(), 20);
        var baixar = $"gestao/titulos/{titulos[0].Id}/baixas";
        Assert.Equal(await gestao.Postar<Baixa>(baixar, pagamento), await gestao.Postar<Baixa>(baixar, pagamento));
        carteira = await campo.Obter<DadosVendedor>("campo/dados");
        Assert.Equal(2, carteira.Historico.Single(x => x.ClienteId == a.Id).Quantidade);
        Assert.Equal(215m, carteira.Dividas.Single(x => x.ClienteId == a.Id).Aberto);
    }

    private sealed class Relogio : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    }
}
