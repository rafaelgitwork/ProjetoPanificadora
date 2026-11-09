using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Panificadora.Api.Comercial;
using Panificadora.Api.Dados;
using Panificadora.Contratos;
using static Panificadora.Api.Comercial.Regras;

namespace Panificadora.Api.Endpoints;

internal static class Rotas
{
    public static void MapGestao(this WebApplication app, string emailGestao)
    {
        var rota = app.MapGroup("/api/gestao").RequireAuthorization(p => p.RequireRole("Gestao"));
        rota.MapGet("/", async (ComercialDb db) => new Painel(
            await db.Estados.ToArrayAsync(), await db.Cidades.ToArrayAsync(), await db.Grupos.ToArrayAsync(),
            await db.Vendedores.ToArrayAsync(), await db.Territorios.ToArrayAsync(), await db.Clientes.ToArrayAsync(),
            await db.Visitas.ToArrayAsync(), await db.Produtos.ToArrayAsync(), await db.Lotes.ToArrayAsync(),
            await db.Formas.ToArrayAsync(), await db.Condicoes.ToArrayAsync(), await db.Prazos.ToArrayAsync(),
            await db.Pedidos.OrderByDescending(p => p.RecebidoEm).ToArrayAsync(), await db.Itens.ToArrayAsync(),
            await db.Vendas.ToArrayAsync(), await db.ItensVenda.ToArrayAsync(), await db.Titulos.ToArrayAsync(),
            await db.Baixas.ToArrayAsync()));
        rota.MapPost("/estados", (Estado e, ComercialDb db) => db.Alterar(async () =>
        {
            Exigir(e.Id != Guid.Empty && Texto(e.Uf, 2) && e.Uf.Trim().Length == 2 && Texto(e.Nome), "Informe nome e UF.");
            return await db.Salvar(e.Id, e with { Uf = e.Uf.Trim().ToUpperInvariant() });
        }));
        rota.MapPost("/cidades", (Cidade c, ComercialDb db) => db.Alterar(async () =>
        {
            Exigir(c.Id != Guid.Empty && Texto(c.Nome) && await db.Estados.AnyAsync(e => e.Id == c.EstadoId),
                "Informe cidade e estado.");
            return await db.Salvar(c.Id, c);
        }));
        rota.MapPost("/grupos", (Grupo g, ComercialDb db) => db.Alterar(async () =>
        {
            Exigir(g.Id != Guid.Empty && Texto(g.Nome), "Informe o grupo.");
            return await db.Salvar(g.Id, g);
        }));
        rota.MapPost("/formas", (Forma f, ComercialDb db) => db.Alterar(async () =>
        {
            Exigir(f.Id != Guid.Empty && Texto(f.Nome), "Informe a forma de pagamento.");
            return await db.Salvar(f.Id, f);
        }));
        rota.MapPost("/vendedores", (Vendedor v, ComercialDb db, PasswordHasher<Vendedor> hasher) => db.Alterar(async () =>
        {
            var email = v.Email?.Trim().ToLowerInvariant();
            Exigir(v.Id != Guid.Empty && Texto(v.Nome) && Texto(email) && email!.Contains('@') && email != emailGestao,
                "Informe nome e e-mail do vendedor.");
            var credencial = await db.Credenciais.FindAsync(v.Id);
            var vendedor = v with { Email = email!, Senha = null };
            if (v.Senha is not null || credencial is null)
            {
                Exigir(v.Senha is { Length: >= 12 and <= 256 }, "A senha deve ter de 12 a 256 caracteres.");
                await db.Salvar(v.Id, new Credencial(v.Id, hasher.HashPassword(vendedor, v.Senha!)));
            }
            return await db.Salvar(v.Id, vendedor);
        }));
        rota.MapPost("/territorios", (Territorio t, ComercialDb db) => db.Alterar(async () =>
        {
            Exigir(t.Id != Guid.Empty && Texto(t.Nome) && await db.Cidades.AnyAsync(c => c.Id == t.CidadeId)
                && await db.Vendedores.AnyAsync(v => v.Id == t.VendedorId && v.Ativo), "Informe cidade, trecho e vendedor ativo.");
            return await db.Salvar(t.Id, t with { Nome = t.Nome.Trim() });
        }));
        rota.MapPost("/clientes", (Cliente c, ComercialDb db, Operacoes op) => db.Alterar(async () => await op.Cliente(c, null)));
        rota.MapPost("/visitas", (Visita v, ComercialDb db) => db.Alterar(async () =>
        {
            var cliente = await db.Clientes.FindAsync(v.ClienteId);
            Exigir(v.Id != Guid.Empty && cliente is { Ativo: true } && Enum.IsDefined(v.Dia) && v.Sequencia > 0,
                "Informe cliente ativo, dia e sequência.");
            var territorio = (await db.Territorios.FindAsync(cliente!.TerritorioId))!;
            var carteira = from c in db.Clientes
                           join t in db.Territorios on c.TerritorioId equals t.Id
                           where t.VendedorId == territorio.VendedorId && c.Ativo
                           select c.Id;
            Exigir(!await db.Visitas.AnyAsync(x => x.Id != v.Id && x.Dia == v.Dia &&
                x.Sequencia == v.Sequencia && carteira.Contains(x.ClienteId)), "A sequência está em uso neste dia.");
            return await db.Salvar(v.Id, v);
        }));
        rota.MapPost("/visitas/{id:guid}/remover", (Guid id, ComercialDb db) => db.Alterar(async () =>
        {
            var visita = await db.Visitas.FindAsync(id);
            if (visita is not null) db.Visitas.Remove(visita);
            return new { Id = id };
        }));
        rota.MapPost("/produtos", (Produto p, ComercialDb db) => db.Alterar(async () =>
        {
            Exigir(p.Id != Guid.Empty && Texto(p.Nome) && Texto(p.Unidade) && Texto(p.Conservacao)
                && Dinheiro(p.Preco) && p.ValidadeMinima is >= 0 and <= 365
                && await db.Grupos.AnyAsync(g => g.Id == p.GrupoId), "Confira os dados do produto.");
            Exigir((p.PrecoPromocional is null && p.PromocaoAte is null) ||
                (p.PrecoPromocional is decimal preco && Dinheiro(preco) && preco < p.Preco && p.PromocaoAte is not null),
                "Informe preço promocional menor que o normal e a data final.");
            return await db.Salvar(p.Id, p);
        }));
        rota.MapPost("/lotes", (Lote l, ComercialDb db, TimeProvider tempo) => db.Alterar(async () =>
        {
            Exigir(l.Id != Guid.Empty && l.Fisico is > 0 and <= 1000000 && l.Reservado == 0
                && l.Validade >= Hoje(tempo.GetUtcNow()) && await db.Produtos.AnyAsync(p => p.Id == l.ProdutoId && p.Ativo),
                "Informe produto ativo, validade e quantidade.");
            Exigir(!await db.Lotes.AnyAsync(x => x.Id == l.Id), "Este lote já foi registrado.");
            db.Lotes.Add(l);
            return l;
        }));
        rota.MapPost("/condicoes", (CadastroCondicao cadastro, ComercialDb db) => db.Alterar(async () =>
        {
            var c = cadastro.Condicao;
            var prazos = cadastro.Prazos;
            Exigir(c is not null && c.Id != Guid.Empty && Texto(c.Nome) && c.Multa is >= 0 and <= 100 &&
                c.JurosDia is >= 0 and <= 100 && c.DescontoDia is >= 0 and <= 100 && prazos is { Length: > 0 and <= 24 }
                && prazos.All(p => p is not null && p.Id != Guid.Empty && p.Dias is >= 0 and <= 3650 &&
                    p.Percentual > 0 && decimal.Round(p.Percentual, 4) == p.Percentual)
                && prazos.Sum(p => p.Percentual) == 100 && prazos.Select(p => p.Id).Distinct().Count() == prazos.Length,
                "Confira os prazos, taxas e percentuais. Os percentuais precisam somar 100%.");
            Exigir(!await db.Condicoes.AnyAsync(x => x.Id == c!.Id), "Cadastre uma nova condição para mudar os prazos.");
            var formas = prazos!.Select(p => p.FormaId).Distinct().ToArray();
            Exigir(await db.Formas.CountAsync(f => formas.Contains(f.Id)) == formas.Length, "Informe a forma de cada parcela.");
            db.Condicoes.Add(c!);
            db.Prazos.AddRange(prazos.Select((p, i) => p with { CondicaoId = c!.Id, Numero = i + 1 }));
            return c!;
        }));
        rota.MapPost("/pedidos/{id:guid}/corte", (Guid id, Corte c, ComercialDb db, Operacoes op) =>
            db.Alterar(async () => await op.Liberar(id, c)));
        rota.MapPost("/vendas/{id:guid}/nota", (Guid id, Faturamento nota, ComercialDb db, Operacoes op) =>
            db.Alterar(async () => await op.Faturar(id, nota)));
        rota.MapPost("/vendas/{id:guid}/expedicao", (Guid id, ComercialDb db, Operacoes op) =>
            db.Alterar(async () => await op.Expedir(id)));
        rota.MapPost("/titulos/{id:guid}/baixas", (Guid id, Pagamento p, ComercialDb db, Operacoes op) =>
            db.Alterar(async () => await op.Pagar(id, p)));
    }

    public static void MapVendas(this WebApplication app)
    {
        var rota = app.MapGroup("/api/campo").RequireAuthorization(p => p.RequireRole("Vendas"));
        rota.MapGet("/dados", async (ClaimsPrincipal usuario, ComercialDb db, TimeProvider tempo) =>
        {
            var vendedor = usuario.VendedorId();
            var hoje = Hoje(tempo.GetUtcNow());
            var territorios = await db.Territorios.Where(t => t.VendedorId == vendedor).ToArrayAsync();
            var ids = territorios.Select(t => t.Id).ToArray();
            var clientes = await db.Clientes.Where(c => c.Ativo && ids.Contains(c.TerritorioId)).ToArrayAsync();
            var carteira = clientes.Select(c => c.Id).ToArray();
            var visitas = await db.Visitas.Where(v => carteira.Contains(v.ClienteId)).OrderBy(v => v.Sequencia).ToArrayAsync();
            var compras = await (from venda in db.Vendas
                                 join item in db.ItensVenda on venda.Id equals item.VendaId
                                 join produto in db.Produtos on item.ProdutoId equals produto.Id
                                 where carteira.Contains(venda.ClienteId) && venda.Nota != null
                                 orderby venda.CriadaEm descending, venda.Id descending
                                 select new UltimaCompra(venda.ClienteId, item.ProdutoId, venda.Data, item.Quantidade, item.Preco, produto.Nome))
                                 .ToArrayAsync();
            var titulos = await (from venda in db.Vendas
                                 join titulo in db.Titulos on venda.Id equals titulo.VendaId
                                 where carteira.Contains(venda.ClienteId) && titulo.Valor > titulo.PrincipalPago
                                 select new { venda.ClienteId, titulo.Vencimento, Saldo = titulo.Valor - titulo.PrincipalPago })
                                 .ToArrayAsync();
            var dividas = titulos.GroupBy(x => x.ClienteId).Select(g => new Divida(g.Key,
                g.Sum(x => x.Saldo), g.Where(x => x.Vencimento < hoje).Sum(x => x.Saldo))).ToArray();
            return new DadosVendedor(hoje, await db.Produtos.Where(p => p.Ativo).ToArrayAsync(), clientes, territorios, visitas,
                await db.Condicoes.ToArrayAsync(), compras.DistinctBy(c => (c.ClienteId, c.ProdutoId)).ToArray(), dividas);
        });
        rota.MapPost("/clientes", (Cliente c, ClaimsPrincipal usuario, ComercialDb db, Operacoes op) =>
            db.Alterar(async () => await op.Cliente(c, usuario.VendedorId())));
        rota.MapPost("/pedidos", (NovoPedido p, ClaimsPrincipal usuario, ComercialDb db, Operacoes op) =>
            db.Alterar(async () => await op.Receber(usuario.VendedorId(), p)));
    }
}
