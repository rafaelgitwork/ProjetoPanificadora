sealed class RegraException(string mensagem) : Exception(mensagem);
sealed record Produto(Guid Id, string Nome, decimal Preco, int ValidadeMinima);
sealed record Cliente(Guid Id, string Nome, DateOnly Desde);
sealed record Lote(Guid Id, Guid ProdutoId, DateOnly Validade, int Fisico, int Reservado);
sealed record NovoProduto(string Nome, decimal Preco, int ValidadeMinima);
sealed record NovoCliente(string Nome, DateOnly Desde);
sealed record NovoLote(Guid ProdutoId, DateOnly Validade, int Quantidade);
sealed record ItemSolicitado(Guid ProdutoId, int Quantidade, decimal Preco);
sealed record EntradaPedido(Guid Id, Guid ClienteId, DateTimeOffset CatalogoEm, int Parcelas, ItemSolicitado[] Itens);
sealed record EntradaPagamento(Guid Id, decimal Valor);
sealed record Pagamento(Guid Id, Guid VendaId, decimal Valor, DateTimeOffset RegistradoEm);
sealed record Pedido(Guid Id, Guid ClienteId, DateOnly Dia, int Parcelas, ItemSolicitado[] Itens, string Status);
sealed record Reserva(Guid LoteId, int Quantidade);
sealed record Parcela(DateOnly Vencimento, decimal Total, decimal Pago);
sealed record Venda(Guid Id, Guid PedidoId, Guid ClienteId, decimal Total, string Status, Reserva[] Reservas, Parcela[] Parcelas);
sealed record Rateio(Guid PedidoId, Guid ProdutoId, DateOnly Dia, int Solicitado, decimal Peso, int Atendido);
sealed class Dados
{
    public List<Produto> Produtos { get; init; } = [];
    public List<Cliente> Clientes { get; init; } = [];
    public List<Lote> Lotes { get; init; } = [];
    public List<Pedido> Pedidos { get; init; } = [];
    public List<Venda> Vendas { get; init; } = [];
    public List<Pagamento> Pagamentos { get; init; } = [];
    public List<Rateio> Rateios { get; init; } = [];
}

static class Operacao
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static DateOnly Hoje(DateTimeOffset agora) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agora, Fuso).DateTime);
    public static DateOnly DiaComercial(DateTimeOffset agora)
    {
        var local = TimeZoneInfo.ConvertTime(agora, Fuso);
        return Hoje(agora).AddDays(local.Hour >= 22 ? 1 : 0);
    }

    public static Pedido Receber(Dados dados, EntradaPedido entrada, DateTimeOffset agora)
    {
        var existente = dados.Pedidos.Find(p => p.Id == entrada.Id);
        if (existente is not null)
        {
            if (existente.ClienteId != entrada.ClienteId || existente.Parcelas != entrada.Parcelas
                || entrada.Itens is null || !existente.Itens.SequenceEqual(entrada.Itens))
                throw new RegraException("Este identificador pertence a outro pedido.");
            return existente;
        }

        if (entrada.CatalogoEm > agora || agora - entrada.CatalogoEm > TimeSpan.FromHours(48))
            throw new RegraException("Atualize o catálogo antes de enviar o pedido.");
        if (entrada.Id == Guid.Empty || !dados.Clientes.Any(c => c.Id == entrada.ClienteId)
            || entrada.Parcelas is < 1 or > 3 || entrada.Itens is not { Length: > 0 and <= 50 }
            || entrada.Itens.Select(i => i.ProdutoId).Distinct().Count() != entrada.Itens.Length)
            throw new RegraException("Confira o cliente, os produtos e o número de parcelas.");

        foreach (var item in entrada.Itens)
        {
            if (item.Quantidade is < 1 or > 10000
                || !dados.Produtos.Any(p => p.Id == item.ProdutoId && p.Preco == item.Preco))
                throw new RegraException("Quantidade inválida ou preço desatualizado.");
        }

        var pedido = new Pedido(entrada.Id, entrada.ClienteId, DiaComercial(agora), entrada.Parcelas, entrada.Itens, "Recebido");
        dados.Pedidos.Add(pedido);
        return pedido;
    }

    public static int Fechar(Dados dados, DateOnly ultimoDia)
    {
        var grupos = dados.Pedidos.Where(p => p.Status == "Recebido" && p.Dia <= ultimoDia)
            .GroupBy(p => p.Dia).OrderBy(g => g.Key).ToArray();
        var fechados = 0;
        foreach (var grupo in grupos)
        {
            var reservas = grupo.ToDictionary(p => p.Id, _ => new List<Reserva>());
            var totais = grupo.ToDictionary(p => p.Id, _ => 0m);
            var demandas = grupo.SelectMany(p => p.Itens.Select(i => (Pedido: p, Item: i))).GroupBy(x => x.Item.ProdutoId);
            foreach (var produtoDemandado in demandas)
            {
                var produto = dados.Produtos.Single(p => p.Id == produtoDemandado.Key);
                var lotes = dados.Lotes.Where(l => l.ProdutoId == produto.Id && l.Validade >= grupo.Key.AddDays(produto.ValidadeMinima)
                    && l.Fisico > l.Reservado).OrderBy(l => l.Validade).ThenBy(l => l.Id).Select(l => l.Id).ToArray();
                var demanda = produtoDemandado.Select(x =>
                {
                    var peso = Peso(dados, x.Pedido.ClienteId, grupo.Key);
                    return new Demanda(x.Pedido.Id, x.Item, peso);
                }).ToArray();
                var disponivel = dados.Lotes.Where(l => lotes.Contains(l.Id)).Sum(l => (long)l.Fisico - l.Reservado);
                Distribuir(demanda, (int)Math.Min(disponivel, demanda.Sum(x => (long)x.Item.Quantidade)));
                foreach (var linha in demanda)
                {
                    dados.Rateios.Add(new(linha.PedidoId, produto.Id, grupo.Key, linha.Item.Quantidade, linha.Peso, linha.Atendido));
                    totais[linha.PedidoId] += linha.Atendido * linha.Item.Preco;
                    var falta = linha.Atendido;
                    foreach (var loteId in lotes)
                    {
                        if (falta == 0) break;
                        var indice = dados.Lotes.FindIndex(l => l.Id == loteId);
                        var lote = dados.Lotes[indice];
                        var quantidade = Math.Min(falta, lote.Fisico - lote.Reservado);
                        if (quantidade == 0) continue;
                        dados.Lotes[indice] = lote with { Reservado = lote.Reservado + quantidade };
                        reservas[linha.PedidoId].Add(new(loteId, quantidade));
                        falta -= quantidade;
                    }
                }
            }

            foreach (var pedido in grupo)
            {
                var total = totais[pedido.Id];
                var posicao = dados.Pedidos.FindIndex(p => p.Id == pedido.Id);
                dados.Pedidos[posicao] = pedido with { Status = total > 0 ? "Fechado" : "Sem estoque" };
                fechados++;
                if (total > 0)
                    dados.Vendas.Add(new(pedido.Id, pedido.Id, pedido.ClienteId, total, "Reservada",
                        reservas[pedido.Id].ToArray(), Parcelar(total, pedido.Parcelas, grupo.Key)));
            }
        }

        return fechados;
    }

    private static decimal Peso(Dados dados, Guid clienteId, DateOnly dia)
    {
        var vencido = dados.Vendas.Where(v => v.ClienteId == clienteId).SelectMany(v => v.Parcelas)
            .Where(p => p.Vencimento < dia).Sum(p => p.Total - p.Pago);
        if (vencido > 0) return 1m / (1m + vencido / 1000m);
        var desde = dados.Clientes.Single(c => c.Id == clienteId).Desde;
        var anos = dia.Year - desde.Year;
        if (desde.AddYears(anos) > dia) anos--;
        return 1.6m * (1 + Math.Clamp(anos, 0, 5) * 0.05m);
    }

    private static void Distribuir(Demanda[] linhas, int saldo)
    {
        while (saldo > 0)
        {
            var abertas = linhas.Where(l => l.Atendido < l.Item.Quantidade).ToArray();
            var soma = abertas.Sum(l => (l.Item.Quantidade - l.Atendido) * l.Peso);
            var quotas = abertas.Select(l => (Linha: l, Quota: Math.Min(l.Item.Quantidade - l.Atendido,
                saldo * (l.Item.Quantidade - l.Atendido) * l.Peso / soma))).ToArray();
            foreach (var (linha, quota) in quotas)
            {
                var inteiro = (int)quota;
                linha.Atendido += inteiro;
                saldo -= inteiro;
            }
            foreach (var (linha, quota) in quotas.OrderByDescending(q => q.Quota % 1)
                .ThenByDescending(q => q.Linha.Peso).ThenBy(q => q.Linha.PedidoId))
            {
                if (saldo == 0) break;
                if (quota % 1 == 0 || linha.Atendido == linha.Item.Quantidade) continue;
                linha.Atendido++;
                saldo--;
            }
        }
    }

    private static Parcela[] Parcelar(decimal total, int quantidade, DateOnly dia)
    {
        var centavos = (long)(total * 100);
        return Enumerable.Range(0, quantidade).Select(i =>
        {
            var vencimento = dia.AddDays(30 * (i + 1));
            while (vencimento.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                vencimento = vencimento.AddDays(1);
            return new Parcela(vencimento, (centavos / quantidade + (i < centavos % quantidade ? 1 : 0)) / 100m, 0);
        }).Where(p => p.Total > 0).ToArray();
    }

    public static Venda Expedir(Dados dados, Guid id, DateTimeOffset agora)
    {
        var posicao = dados.Vendas.FindIndex(v => v.Id == id);
        if (posicao < 0) throw new RegraException("Venda não encontrada.");
        var venda = dados.Vendas[posicao];
        if (venda.Status == "Em rota") return venda;
        foreach (var reserva in venda.Reservas)
        {
            var indice = dados.Lotes.FindIndex(l => l.Id == reserva.LoteId);
            var lote = dados.Lotes[indice];
            var produto = dados.Produtos.Single(p => p.Id == lote.ProdutoId);
            if (lote.Validade < Hoje(agora).AddDays(produto.ValidadeMinima))
                throw new RegraException("Há lote reservado fora da validade mínima. Saída bloqueada.");
            dados.Lotes[indice] = lote with { Fisico = lote.Fisico - reserva.Quantidade, Reservado = lote.Reservado - reserva.Quantidade };
        }

        return dados.Vendas[posicao] = venda with { Status = "Em rota" };
    }

    public static Pagamento Pagar(Dados dados, Guid id, EntradaPagamento entrada)
    {
        var anterior = dados.Pagamentos.Find(p => p.Id == entrada.Id);
        if (anterior is not null)
        {
            if (anterior.VendaId != id || anterior.Valor != entrada.Valor)
                throw new RegraException("Identificador de pagamento em uso.");
            return anterior;
        }
        var posicao = dados.Vendas.FindIndex(v => v.Id == id);
        if (posicao < 0) throw new RegraException("Venda não encontrada.");
        var venda = dados.Vendas[posicao];
        var saldo = venda.Parcelas.Sum(p => p.Total - p.Pago);
        if (entrada.Id == Guid.Empty || entrada.Valor <= 0 || entrada.Valor > saldo || decimal.Round(entrada.Valor, 2) != entrada.Valor)
            throw new RegraException("Informe um valor positivo, com até dois centavos decimais e dentro do saldo.");
        var restante = entrada.Valor;
        var parcelas = venda.Parcelas.Select(p =>
        {
            var valor = Math.Min(restante, p.Total - p.Pago);
            restante -= valor;
            return p with { Pago = p.Pago + valor };
        }).ToArray();
        dados.Vendas[posicao] = venda with { Parcelas = parcelas };
        var pagamento = new Pagamento(entrada.Id, id, entrada.Valor, DateTimeOffset.UtcNow);
        dados.Pagamentos.Add(pagamento);
        return pagamento;
    }

    private sealed class Demanda(Guid pedidoId, ItemSolicitado item, decimal peso)
    {
        public Guid PedidoId { get; } = pedidoId;
        public ItemSolicitado Item { get; } = item;
        public decimal Peso { get; } = peso;
        public int Atendido { get; set; }
    }
}
