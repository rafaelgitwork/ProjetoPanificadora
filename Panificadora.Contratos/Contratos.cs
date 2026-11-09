namespace Panificadora.Contratos;

public record Acesso(string Email, string Senha);
public record Sessao(string Token, Guid Id, string Nome, string Perfil);
public record Estado(Guid Id, string Uf, string Nome);
public record Cidade(Guid Id, Guid EstadoId, string Nome);
public record Grupo(Guid Id, string Nome);
public record Forma(Guid Id, string Nome);
public record Vendedor(Guid Id, string Nome, string Email, bool Ativo) { public string? Senha { get; init; } }
public record Territorio(Guid Id, Guid CidadeId, string Nome, Guid VendedorId);
public record Cliente(Guid Id, Guid TerritorioId, string Nome, string Documento, string Email,
    string Telefone, string Endereco, bool Ativo);
public record Visita(Guid Id, Guid ClienteId, DayOfWeek Dia, int Sequencia);
public record Produto(Guid Id, Guid GrupoId, string Nome, string Unidade, string Conservacao,
    decimal Preco, int ValidadeMinima, DateOnly Lancamento, bool Ativo,
    decimal? PrecoPromocional = null, DateOnly? PromocaoAte = null);
public record Lote(Guid Id, Guid ProdutoId, DateOnly Validade, int Fisico, int Reservado = 0);
public record Condicao(Guid Id, string Nome, decimal Multa, decimal JurosDia, decimal DescontoDia);
public record Prazo(Guid Id, Guid CondicaoId, int Numero, int Dias, decimal Percentual, Guid FormaId);
public record CadastroCondicao(Condicao Condicao, Prazo[] Prazos);
public record ItemPedido(Guid ProdutoId, int Quantidade, decimal Preco);
public record NovoPedido(Guid Id, Guid ClienteId, Guid CondicaoId, ItemPedido[] Itens);
public record Pedido(Guid Id, Guid ClienteId, Guid VendedorId, Guid CondicaoId,
    DateTimeOffset RecebidoEm, string Situacao);
public record LinhaPedido(Guid Id, Guid PedidoId, Guid ProdutoId, int Quantidade, decimal Preco, int? Liberado = null);
public record Liberacao(Guid LinhaId, int Quantidade);
public record Corte(Liberacao[] Itens);
public record Venda(Guid Id, Guid PedidoId, Guid ClienteId, DateOnly Data, DateTimeOffset CriadaEm, decimal Total, string Situacao, string? Nota = null);
public record LinhaVenda(Guid Id, Guid VendaId, Guid ProdutoId, int Quantidade, decimal Preco);
public record Reserva(Guid Id, Guid VendaId, Guid LoteId, int Quantidade);
public record Titulo(Guid Id, Guid VendaId, int Numero, string Forma, DateOnly Vencimento,
    decimal Valor, decimal PrincipalPago, decimal Multa, decimal JurosDia, decimal DescontoDia);
public record Baixa(Guid Id, Guid TituloId, DateOnly Data, decimal Valor, decimal Principal,
    decimal Encargos, decimal Desconto);
public record Pagamento(Guid Id, decimal Valor);
public record Faturamento(string Numero);
public record UltimaCompra(Guid ClienteId, Guid ProdutoId, DateOnly Data, int Quantidade, decimal Preco, string NomeProduto);
public record Divida(Guid ClienteId, decimal Aberto, decimal Vencido);
public record DadosVendedor(DateOnly Hoje, Produto[] Produtos, Cliente[] Clientes, Territorio[] Territorios,
    Visita[] Visitas, Condicao[] Condicoes, UltimaCompra[] Historico, Divida[] Dividas);
public record Painel(Estado[] Estados, Cidade[] Cidades, Grupo[] Grupos, Vendedor[] Vendedores,
    Territorio[] Territorios, Cliente[] Clientes, Visita[] Visitas, Produto[] Produtos, Lote[] Lotes,
    Forma[] Formas, Condicao[] Condicoes, Prazo[] Prazos, Pedido[] Pedidos, LinhaPedido[] Itens,
    Venda[] Vendas, LinhaVenda[] ItensVenda, Titulo[] Titulos, Baixa[] Baixas);
