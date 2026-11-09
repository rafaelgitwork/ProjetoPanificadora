using System.Globalization;
using System.Text.Json;
using Panificadora.App.Servicos;
using Panificadora.Contratos;

namespace Panificadora.App;

public sealed class CampoPage : ContentPage
{
    private static readonly CultureInfo Brasil = new("pt-BR");
    private readonly Trabalho trabalho;
    private readonly VerticalStackLayout conteudo = new() { Spacing = 12 };
    private readonly Label aviso = new() { FontAttributes = FontAttributes.Bold };
    private readonly Dictionary<Guid, int> itens = [];
    private readonly IDispatcherTimer timer;
    private Guid clientePedido;
    private Guid condicaoPedido;
    private Guid pedidoEmRevisao;
    private string aba = "Roteiro";
    private bool ocupado;
    private bool iniciado;
    private DateTimeOffset proximaTentativa = DateTimeOffset.MinValue;

    public CampoPage(Trabalho trabalho)
    {
        this.trabalho = trabalho;
        Title = "Panificadora do Rafael";
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 20, Spacing = 12, Children = { Titulo("Panificadora do Rafael"), aviso, conteudo }
        } };
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(20);
        timer.Tick += async (_, _) =>
        {
            var limite = Trabalho.InicioDaJanela;
            if (ocupado || pedidoEmRevisao != Guid.Empty || trabalho.Sessao is null || limite is null ||
                Connectivity.Current.NetworkAccess != NetworkAccess.Internet || DateTimeOffset.UtcNow < proximaTentativa ||
                !trabalho.Fila.Any(x => x.CriadoEm < limite)) return;
            proximaTentativa = DateTimeOffset.UtcNow.AddMinutes(2);
            await Executar(() => Transmitir(limite));
        };
        Loaded += async (_, _) =>
        {
            if (!iniciado)
            {
                iniciado = true;
                await Executar(trabalho.Restaurar, true);
            }
            timer.Start();
        };
        Unloaded += (_, _) => timer.Stop();
        Renderizar();
    }

    private static Label Titulo(string texto) => new() { Text = texto, FontSize = 22, FontAttributes = FontAttributes.Bold };
    private void Linha(string texto) => conteudo.Children.Add(new Label { Text = texto });
    private static string Dinheiro(decimal valor) => valor.ToString("C", Brasil);
    private void Botao(string texto, Func<Task> acao, bool refazer = false)
    {
        var botao = new Button { Text = texto };
        botao.Clicked += async (_, _) => await Executar(acao, refazer);
        conteudo.Children.Add(botao);
    }

    private async Task Executar(Func<Task> acao, bool refazer = false)
    {
        if (ocupado) return;
        ocupado = true;
        aviso.Text = "";
        try { await acao(); }
        catch (Exception ex) { aviso.Text = ex.Message; }
        finally
        {
            ocupado = false;
            if (refazer) Renderizar();
        }
    }

    private void Renderizar()
    {
        conteudo.Children.Clear();
        if (trabalho.Sessao is null)
        {
            var endereco = Campo("URL da API", "http://localhost:5100/");
            var email = Campo("E-mail", teclado: Keyboard.Email);
            var senha = Campo("Senha", senha: true);
            Botao("Entrar", async () =>
            {
                await trabalho.Entrar(endereco.Text ?? "", email.Text ?? "", senha.Text ?? "");
                senha.Text = "";
                Renderizar();
                await trabalho.Baixar();
                Renderizar();
            });
            return;
        }

        Linha($"Vendedor: {trabalho.Sessao.Nome} · {trabalho.Fila.Length} na fila");
        foreach (var opcao in new[] { "Roteiro", "Novo cliente", "Pedido", "Fila" })
            Botao(opcao, () => { aba = opcao; Renderizar(); return Task.CompletedTask; });
        Botao("Baixar preços, carteira e roteiro", async () => { await trabalho.Baixar(); Renderizar(); aviso.Text = "Dados atualizados."; });
        Botao("Trocar conta ou renovar sessão", async () =>
        {
            if (!await DisplayAlertAsync("Sessão", "O catálogo e os registros pendentes desta conta ficam no aparelho.", "Continuar", "Cancelar")) return;
            trabalho.Sair();
            itens.Clear();
            clientePedido = Guid.Empty;
            condicaoPedido = Guid.Empty;
            pedidoEmRevisao = Guid.Empty;
            Renderizar();
        });
        switch (aba)
        {
            case "Roteiro": Roteiro(); break;
            case "Novo cliente": Cadastro(); break;
            case "Pedido": Pedido(); break;
            case "Fila": Pendentes(); break;
        }
    }

    private Entry Campo(string rotulo, string? valor = null, bool senha = false, Keyboard? teclado = null)
    {
        Linha(rotulo);
        var entrada = new Entry { Text = valor, Placeholder = rotulo, IsPassword = senha, Keyboard = teclado ?? Keyboard.Default };
        conteudo.Children.Add(entrada);
        return entrada;
    }

    private Picker Seletor<T>(string titulo, T[] valores, Func<T, string> nome, int selecionado = -1)
    {
        var seletor = new Picker { Title = titulo, ItemsSource = valores.Select(nome).ToList(), SelectedIndex = selecionado };
        conteudo.Children.Add(seletor);
        return seletor;
    }

    private void Roteiro()
    {
        var hoje = Trabalho.Hoje.DayOfWeek;
        var dados = trabalho.Dados;
        var clientes = trabalho.Carteira();
        conteudo.Children.Add(Titulo("Roteiro de " + Brasil.DateTimeFormat.GetDayName(hoje)));
        var visitas = dados?.Visitas.Where(v => v.Dia == hoje).OrderBy(v => v.Sequencia).ToArray() ?? [];
        if (visitas.Length == 0) Linha("Nenhuma visita programada hoje.");
        foreach (var visita in visitas)
        {
            var cliente = clientes.FirstOrDefault(c => c.Id == visita.ClienteId);
            if (cliente is not null) AbrirCliente($"{visita.Sequencia} · {cliente.Nome} · {cliente.Endereco}", cliente);
        }
        conteudo.Children.Add(Titulo("Carteira de clientes"));
        foreach (var cliente in clientes) AbrirCliente(cliente.Nome, cliente);

        void AbrirCliente(string rotulo, Cliente cliente) => Botao(rotulo, () =>
        {
            clientePedido = cliente.Id;
            pedidoEmRevisao = Guid.Empty;
            itens.Clear();
            aba = "Pedido";
            Renderizar();
            return Task.CompletedTask;
        });
    }

    private void Cadastro()
    {
        conteudo.Children.Add(Titulo("Nova panificadora"));
        var territorios = trabalho.Dados?.Territorios ?? [];
        var territorio = Seletor("Território", territorios, t => t.Nome);
        var nome = Campo("Nome da empresa");
        var documento = Campo("CPF ou CNPJ");
        var email = Campo("E-mail", teclado: Keyboard.Email);
        var telefone = Campo("Telefone", teclado: Keyboard.Telephone);
        var endereco = Campo("Endereço");
        Botao("Guardar cadastro na fila", () =>
        {
            var campos = new[] { nome.Text, documento.Text, email.Text, telefone.Text, endereco.Text };
            if (territorio.SelectedIndex < 0 || campos.Any(string.IsNullOrWhiteSpace) ||
                documento.Text.Count(char.IsAsciiDigit) is not (11 or 14))
                throw new InvalidOperationException("Preencha os dados da empresa, o documento e o território.");
            var cliente = new Cliente(Guid.NewGuid(), territorios[territorio.SelectedIndex].Id, nome.Text, documento.Text,
                email.Text, telefone.Text, endereco.Text, true);
            trabalho.Guardar(cliente);
            clientePedido = cliente.Id;
            pedidoEmRevisao = Guid.Empty;
            aba = "Pedido";
            Renderizar();
            aviso.Text = "Cadastro guardado. Já é possível registrar um pedido para esse cliente.";
            return Task.CompletedTask;
        });
    }

    private void Pedido()
    {
        conteudo.Children.Add(Titulo("Pedido em campo"));
        var dados = trabalho.Dados;
        if (dados is null) { Linha("Baixe os dados antes de registrar pedidos."); return; }
        var clientes = trabalho.Carteira();
        if (!clientes.Any(c => c.Id == clientePedido)) clientePedido = Guid.Empty;
        if (!dados.Condicoes.Any(c => c.Id == condicaoPedido)) condicaoPedido = Guid.Empty;
        var cliente = Seletor("Cliente", clientes, c => c.Nome, Array.FindIndex(clientes, c => c.Id == clientePedido));
        cliente.SelectedIndexChanged += (_, _) =>
        {
            if (cliente.SelectedIndex < 0) return;
            clientePedido = clientes[cliente.SelectedIndex].Id;
            itens.Clear();
            Renderizar();
        };
        if (clientePedido != Guid.Empty)
        {
            var divida = dados.Dividas.FirstOrDefault(d => d.ClienteId == clientePedido);
            Linha($"Em aberto: {Dinheiro(divida?.Aberto ?? 0)} · vencido: {Dinheiro(divida?.Vencido ?? 0)}");
            Linha("Última compra por produto");
            foreach (var historico in dados.Historico.Where(h => h.ClienteId == clientePedido))
                Linha($"{dados.Produtos.FirstOrDefault(p => p.Id == historico.ProdutoId)?.Nome ?? historico.NomeProduto} · " +
                    $"{historico.Quantidade} un. · {Dinheiro(historico.Preco)} · {historico.Data:dd/MM/yyyy}");
        }

        var condicao = Seletor("Condição de pagamento", dados.Condicoes, c => c.Nome,
            Array.FindIndex(dados.Condicoes, c => c.Id == condicaoPedido));
        condicao.SelectedIndexChanged += (_, _) =>
        {
            if (condicao.SelectedIndex >= 0) condicaoPedido = dados.Condicoes[condicao.SelectedIndex].Id;
        };
        var produtos = dados.Produtos.OrderByDescending(p => Promocao(p)).ThenBy(p => p.Nome).ToArray();
        foreach (var produto in produtos.Where(p => Promocao(p) || Novo(p)))
        {
            var etiquetas = (Promocao(produto) ? "PROMOÇÃO " : "") + (Novo(produto) ? "NOVO" : "");
            conteudo.Children.Add(new Label
            {
                Text = $"{etiquetas} · {produto.Nome}", TextColor = Colors.Black,
                BackgroundColor = Promocao(produto) ? Colors.Gold : Colors.LightSkyBlue
            });
        }
        var seletor = Seletor("Produto", produtos, p => $"{p.Nome} · {p.Unidade} · {Dinheiro(Preco(p))}");
        var quantidade = Campo("Quantidade", "1", teclado: Keyboard.Numeric);
        Botao("Adicionar item", () =>
        {
            if (clientePedido == Guid.Empty || seletor.SelectedIndex < 0 || !int.TryParse(quantidade.Text, out var q)
                || q is < 1 or > 10000)
                throw new InvalidOperationException("Escolha cliente, produto e uma quantidade válida.");
            var id = produtos[seletor.SelectedIndex].Id;
            var soma = itens.GetValueOrDefault(id) + q;
            if (soma > 10000) throw new InvalidOperationException("Limite de 10.000 unidades por item.");
            itens[id] = soma;
            Renderizar();
            return Task.CompletedTask;
        });
        if (pedidoEmRevisao != Guid.Empty)
        {
            Linha("Revisando pedido retido com os preços atuais.");
            Botao("Cancelar revisão", () =>
            {
                pedidoEmRevisao = Guid.Empty;
                itens.Clear();
                aba = "Fila";
                Renderizar();
                return Task.CompletedTask;
            });
        }
        foreach (var (id, q) in itens.ToArray())
        {
            var nome = produtos.FirstOrDefault(p => p.Id == id)?.Nome ?? "produto fora do catálogo";
            Botao($"Remover {q} × {nome}", () => { itens.Remove(id); Renderizar(); return Task.CompletedTask; });
        }
        Botao("Guardar pedido na fila", () =>
        {
            if (clientePedido == Guid.Empty || condicaoPedido == Guid.Empty || itens.Count == 0)
                throw new InvalidOperationException("Informe cliente, condição e itens.");
            if (itens.Keys.Any(id => produtos.All(p => p.Id != id)))
                throw new InvalidOperationException("Remova os produtos que saíram do catálogo.");
            var pedido = new NovoPedido(pedidoEmRevisao == Guid.Empty ? Guid.NewGuid() : pedidoEmRevisao, clientePedido,
                condicaoPedido, itens.Select(i => new ItemPedido(i.Key, i.Value, Preco(produtos.Single(p => p.Id == i.Key)))).ToArray());
            if (pedidoEmRevisao == Guid.Empty) trabalho.Guardar(pedido);
            else trabalho.Revisar(pedido);
            pedidoEmRevisao = Guid.Empty;
            itens.Clear();
            aba = "Fila";
            Renderizar();
            aviso.Text = "Pedido salvo neste aparelho. Ainda não foi transmitido.";
            return Task.CompletedTask;
        });
    }

    private void Pendentes()
    {
        conteudo.Children.Add(Titulo("Fila de envio"));
        Linha("Janelas automáticas às 12h e 19h, com o aplicativo aberto e conectado.");
        foreach (var envio in trabalho.Fila)
        {
            var nome = envio.Tipo == "clientes"
                ? JsonSerializer.Deserialize<Cliente>(envio.Corpo)!.Nome
                : $"{envio.Id.ToString()[..8]} · {JsonSerializer.Deserialize<NovoPedido>(envio.Corpo)!.Itens.Length} item(ns)";
            Linha($"{(envio.Tipo == "clientes" ? "Cliente" : "Pedido")} · {nome} · {envio.Erro ?? "pendente"}");
            if (envio.Tipo == "pedidos" && envio.Erro is not null)
            {
                Botao("Atualizar catálogo e revisar " + nome, async () =>
                {
                    await trabalho.Baixar();
                    var pedido = JsonSerializer.Deserialize<NovoPedido>(envio.Corpo)!;
                    (pedidoEmRevisao, clientePedido, condicaoPedido) = (pedido.Id, pedido.ClienteId, pedido.CondicaoId);
                    itens.Clear();
                    foreach (var item in pedido.Itens) itens[item.ProdutoId] = item.Quantidade;
                    aba = "Pedido";
                    Renderizar();
                });
            }
        }
        Botao("Enviar pedidos e clientes pendentes", () => Transmitir());
    }

    private async Task Transmitir(long? limite = null)
    {
        var resultado = await trabalho.Enviar(limite);
        try { await trabalho.Baixar(); }
        catch (Exception ex) { resultado += " Dados locais mantidos. " + ex.Message; }
        if (aba == "Fila") Renderizar();
        aviso.Text = resultado;
    }

    private static bool Promocao(Produto p) => p.PrecoPromocional is > 0 && p.PromocaoAte >= Trabalho.Hoje;
    private static bool Novo(Produto p) => p.Lancamento <= Trabalho.Hoje && p.Lancamento.AddDays(30) > Trabalho.Hoje;
    private static decimal Preco(Produto p) => Promocao(p) ? p.PrecoPromocional!.Value : p.Preco;
}
