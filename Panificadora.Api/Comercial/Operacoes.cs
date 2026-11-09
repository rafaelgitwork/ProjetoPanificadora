using Microsoft.EntityFrameworkCore;
using Panificadora.Api.Dados;
using Panificadora.Contratos;

namespace Panificadora.Api.Comercial;

public sealed class RegraException(string mensagem) : Exception(mensagem);

public static class Regras
{
    public static DateOnly Hoje(DateTimeOffset instante) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(instante, "America/Sao_Paulo").DateTime);

    public static void Exigir(bool condicao, string mensagem)
    {
        if (!condicao) throw new RegraException(mensagem);
    }

    public static bool Texto(string? valor, int maximo = 250) =>
        !string.IsNullOrWhiteSpace(valor) && valor.Trim().Length <= maximo;
    public static bool Dinheiro(decimal valor) => valor is > 0 and <= 100000000m && decimal.Round(valor, 2) == valor;
    public static bool Promocao(Produto produto, DateOnly hoje) =>
        produto.PrecoPromocional is > 0 && produto.PromocaoAte >= hoje;
    public static decimal Preco(Produto produto, DateOnly hoje) =>
        Promocao(produto, hoje) ? produto.PrecoPromocional!.Value : produto.Preco;

    public static decimal[] Parcelar(decimal total, IReadOnlyList<decimal> percentuais)
    {
        Exigir(Dinheiro(total) && percentuais.Count > 0 && percentuais.All(p => p > 0)
            && percentuais.Sum() == 100, "Percentuais inválidos.");
        var centavos = (long)(total * 100);
        var quotas = percentuais.Select(p => centavos * p / 100).ToArray();
        var partes = quotas.Select(q => (long)q).ToArray();
        var resto = (int)(centavos - partes.Sum());
        foreach (var indice in Enumerable.Range(0, partes.Length)
                     .OrderByDescending(i => quotas[i] - partes[i]).ThenBy(i => i).Take(resto))
            partes[indice]++;
        Exigir(partes.All(p => p > 0), "O total é insuficiente para essa condição de pagamento.");
        return partes.Select(p => p / 100m).ToArray();
    }

    public static (decimal Encargos, decimal Desconto, decimal Principal) Baixar(
        Titulo titulo, IReadOnlyList<Baixa> baixas, DateOnly hoje, decimal valor)
    {
        Exigir(Dinheiro(valor), "Informe um valor de pagamento válido.");
        var principal = titulo.Valor - titulo.PrincipalPago;
        var atraso = Math.Max(0, hoje.DayNumber - titulo.Vencimento.DayNumber);
        var antecipacao = Math.Max(0, titulo.Vencimento.DayNumber - hoje.DayNumber);
        var pagasEmAtraso = baixas.Where(b => b.Data > titulo.Vencimento).OrderBy(b => b.Data).ToArray();
        var baseJuros = principal + pagasEmAtraso.Sum(b => b.Principal);
        var cursor = titulo.Vencimento;
        var juros = 0m;
        foreach (var baixa in pagasEmAtraso)
        {
            juros += baseJuros * titulo.JurosDia / 100 * (baixa.Data.DayNumber - cursor.DayNumber);
            baseJuros -= baixa.Principal;
            cursor = baixa.Data;
        }
        juros += baseJuros * titulo.JurosDia / 100 * Math.Max(0, hoje.DayNumber - cursor.DayNumber);
        var multa = atraso == 0 ? 0 : (principal + pagasEmAtraso.Sum(b => b.Principal)) * titulo.Multa / 100;
        var encargos = Math.Max(0, decimal.Round(multa + juros, 2) - baixas.Sum(b => b.Encargos));
        var desconto = decimal.Round(Math.Min(principal * antecipacao * titulo.DescontoDia / 100,
            Math.Max(0, principal - 0.01m)), 2);
        if (valor == principal - desconto && desconto > 0) return (0, desconto, principal);
        Exigir(principal > 0 && valor <= principal + encargos, "O pagamento ultrapassa o saldo do título.");
        var apropriados = Math.Min(encargos, valor);
        return (apropriados, 0, valor - apropriados);
    }
}

public sealed class Operacoes(ComercialDb db, TimeProvider relogio)
{
    private DateOnly Hoje => Regras.Hoje(relogio.GetUtcNow());

    public async Task<Cliente> Cliente(Cliente cliente, Guid? vendedor)
    {
        Regras.Exigir(cliente.Id != Guid.Empty && Regras.Texto(cliente.Nome) && Regras.Texto(cliente.Documento, 32)
            && Regras.Texto(cliente.Email) && Regras.Texto(cliente.Telefone, 40) && Regras.Texto(cliente.Endereco), "Preencha os dados da empresa.");
        var documento = new string(cliente.Documento.Where(char.IsAsciiDigit).ToArray());
        Regras.Exigir(documento.Length is 11 or 14, "Informe CPF ou CNPJ.");
        var territorio = await db.Territorios.FindAsync(cliente.TerritorioId);
        Regras.Exigir(territorio is not null && (vendedor is null || territorio.VendedorId == vendedor),
            "A região não pertence à carteira.");
        var anterior = await db.Clientes.FindAsync(cliente.Id);
        if (vendedor is not null)
        {
            Regras.Exigir(cliente.Ativo, "O cadastro em campo deve estar ativo.");
            if (anterior is not null)
            {
                Regras.Exigir(anterior.Documento == documento && anterior.TerritorioId == cliente.TerritorioId,
                    "O identificador pertence a outro cadastro.");
                return anterior;
            }
        }
        return await db.Salvar(cliente.Id, cliente with { Documento = documento });
    }

    public async Task<Pedido> Receber(Guid vendedor, NovoPedido entrada)
    {
        Regras.Exigir(entrada.Id != Guid.Empty && entrada.Itens is { Length: > 0 and <= 100 }
            && entrada.Itens.All(i => i is not null && i.Quantidade is > 0 and <= 10000)
            && entrada.Itens.Select(i => i.ProdutoId).Distinct().Count() == entrada.Itens.Length, "Confira os itens do pedido.");
        var anterior = await db.Pedidos.FindAsync(entrada.Id);
        if (anterior is not null)
        {
            var itens = await db.Itens.Where(i => i.PedidoId == anterior.Id).ToArrayAsync();
            Regras.Exigir(anterior.VendedorId == vendedor && anterior.ClienteId == entrada.ClienteId &&
                anterior.CondicaoId == entrada.CondicaoId && itens.Length == entrada.Itens.Length &&
                entrada.Itens.All(i => itens.Any(x => x.ProdutoId == i.ProdutoId && x.Quantidade == i.Quantidade && x.Preco == i.Preco)),
                "Identificador de pedido em uso.");
            return anterior;
        }

        var cliente = await db.Clientes.FindAsync(entrada.ClienteId);
        Regras.Exigir(cliente is { Ativo: true } && await db.Territorios.AnyAsync(t =>
            t.Id == cliente.TerritorioId && t.VendedorId == vendedor), "Cliente fora da carteira.");
        Regras.Exigir(await db.Condicoes.AnyAsync(c => c.Id == entrada.CondicaoId), "Escolha uma condição de pagamento.");
        var produtosIds = entrada.Itens.Select(i => i.ProdutoId).ToArray();
        var produtos = await db.Produtos.Where(p => p.Ativo && produtosIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var item in entrada.Itens)
        {
            Regras.Exigir(produtos.TryGetValue(item.ProdutoId, out var produto) && Regras.Preco(produto, Hoje) == item.Preco,
                "Produto indisponível ou preço alterado. Atualize o catálogo e revise o pedido.");
            db.Itens.Add(new(Guid.NewGuid(), entrada.Id, item.ProdutoId, item.Quantidade, item.Preco));
        }
        var pedido = new Pedido(entrada.Id, entrada.ClienteId, vendedor, entrada.CondicaoId, relogio.GetUtcNow(), "Recebido");
        db.Pedidos.Add(pedido);
        return pedido;
    }

    public async Task<Pedido> Liberar(Guid id, Corte corte)
    {
        var pedido = await db.Pedidos.FindAsync(id) ?? throw new RegraException("Pedido inexistente.");
        Regras.Exigir(pedido.Situacao == "Recebido", "O pedido já foi analisado.");
        var linhas = await db.Itens.Where(i => i.PedidoId == id).ToArrayAsync();
        Regras.Exigir(corte.Itens is not null && corte.Itens.Length == linhas.Length &&
            corte.Itens.All(i => i is not null) && corte.Itens.Select(i => i.LinhaId).Distinct().Count() == linhas.Length,
            "Informe a quantidade liberada de todos os itens.");
        var vendaId = Guid.NewGuid();
        var total = 0m;
        foreach (var linha in linhas)
        {
            var liberacao = corte.Itens!.SingleOrDefault(x => x.LinhaId == linha.Id);
            Regras.Exigir(liberacao is not null && liberacao.Quantidade >= 0 && liberacao.Quantidade <= linha.Quantidade,
                "A quantidade liberada deve ficar entre zero e a solicitada.");
            var quantidade = liberacao!.Quantidade;
            await db.Salvar(linha.Id, linha with { Liberado = quantidade });
            if (quantidade == 0) continue;
            var produto = (await db.Produtos.FindAsync(linha.ProdutoId))!;
            var validade = Hoje.AddDays(produto.ValidadeMinima);
            var lotes = await db.Lotes.Where(l => l.ProdutoId == produto.Id && l.Validade >= validade &&
                l.Fisico > l.Reservado).OrderBy(l => l.Validade).ThenBy(l => l.Id).ToArrayAsync();
            var falta = quantidade;
            foreach (var lote in lotes)
            {
                var separado = Math.Min(falta, lote.Fisico - lote.Reservado);
                await db.Salvar(lote.Id, lote with { Reservado = lote.Reservado + separado });
                db.Reservas.Add(new(Guid.NewGuid(), vendaId, lote.Id, separado));
                falta -= separado;
                if (falta == 0) break;
            }
            Regras.Exigir(falta == 0, $"Saldo elegível insuficiente para {produto.Nome}.");
            db.ItensVenda.Add(new(Guid.NewGuid(), vendaId, produto.Id, quantidade, linha.Preco));
            total += quantidade * linha.Preco;
        }
        if (total > 0)
        {
            var condicao = (await db.Condicoes.FindAsync(pedido.CondicaoId))!;
            var prazos = await db.Prazos.Where(p => p.CondicaoId == condicao.Id).OrderBy(p => p.Numero).ToArrayAsync();
            var valores = Regras.Parcelar(total, prazos.Select(p => p.Percentual).ToArray());
            db.Vendas.Add(new(vendaId, id, pedido.ClienteId, Hoje, relogio.GetUtcNow(), total, "Reservada"));
            for (var i = 0; i < prazos.Length; i++)
            {
                var prazo = prazos[i];
                var forma = (await db.Formas.FindAsync(prazo.FormaId))!;
                db.Titulos.Add(new(Guid.NewGuid(), vendaId, prazo.Numero, forma.Nome, Hoje.AddDays(prazo.Dias),
                    valores[i], 0, condicao.Multa, condicao.JurosDia, condicao.DescontoDia));
            }
        }
        return await db.Salvar(id, pedido with { Situacao = total == 0 ? "Sem atendimento" : "Convertido" });
    }

    public async Task<Venda> Faturar(Guid id, Faturamento entrada)
    {
        var venda = await db.Vendas.FindAsync(id) ?? throw new RegraException("Venda inexistente.");
        Regras.Exigir(Regras.Texto(entrada.Numero, 60), "Informe o número da nota.");
        var numero = entrada.Numero.Trim();
        if (venda.Nota == numero) return venda;
        Regras.Exigir(venda.Nota is null && !await db.Vendas.AnyAsync(v => v.Nota == numero),
            "A venda já tem nota ou o número está em uso.");
        return await db.Salvar(id, venda with { Nota = numero, Situacao = "Faturada" });
    }

    public async Task<Venda> Expedir(Guid id)
    {
        var venda = await db.Vendas.FindAsync(id) ?? throw new RegraException("Venda inexistente.");
        if (venda.Situacao == "Em rota") return venda;
        Regras.Exigir(venda.Nota is not null, "Registre a nota antes da saída.");
        foreach (var reserva in await db.Reservas.Where(r => r.VendaId == id).ToArrayAsync())
        {
            var lote = (await db.Lotes.FindAsync(reserva.LoteId))!;
            var produto = (await db.Produtos.FindAsync(lote.ProdutoId))!;
            Regras.Exigir(lote.Validade >= Hoje.AddDays(produto.ValidadeMinima) && lote.Reservado >= reserva.Quantidade,
                "Um lote reservado está fora da validade ou sem saldo.");
            await db.Salvar(lote.Id, lote with { Fisico = lote.Fisico - reserva.Quantidade, Reservado = lote.Reservado - reserva.Quantidade });
        }
        return await db.Salvar(id, venda with { Situacao = "Em rota" });
    }

    public async Task<Baixa> Pagar(Guid id, Pagamento entrada)
    {
        Regras.Exigir(entrada.Id != Guid.Empty, "Identificador de pagamento inválido.");
        var anterior = await db.Baixas.FindAsync(entrada.Id);
        if (anterior is not null)
        {
            Regras.Exigir(anterior.TituloId == id && anterior.Valor == entrada.Valor, "Identificador em uso.");
            return anterior;
        }
        var titulo = await db.Titulos.FindAsync(id) ?? throw new RegraException("Título inexistente.");
        var baixas = await db.Baixas.Where(b => b.TituloId == id).ToArrayAsync();
        var (encargos, desconto, principal) = Regras.Baixar(titulo, baixas, Hoje, entrada.Valor);
        var baixa = new Baixa(entrada.Id, id, Hoje, entrada.Valor, principal, encargos, desconto);
        db.Baixas.Add(baixa);
        await db.Salvar(id, titulo with { PrincipalPago = titulo.PrincipalPago + principal });
        return baixa;
    }
}
