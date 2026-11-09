using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var adminKey = builder.Configuration["PANIFICADORA_ADMIN"];
var sellerKey = builder.Configuration["PANIFICADORA_VENDAS"];
if (new[] { adminKey, sellerKey }.Any(key => string.IsNullOrWhiteSpace(key) || key.Length < 12)
    || adminKey == sellerKey)
    throw new InvalidOperationException("Configure PANIFICADORA_ADMIN e PANIFICADORA_VENDAS com chaves distintas de pelo menos 12 caracteres.");

builder.Services.AddSingleton<Registro>();
builder.Services.AddHostedService<FechamentoAutomatico>();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next(context);
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    var supplied = context.Request.Headers["X-Acesso"].ToString();
    bool Confere(string key) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(key));
    var administrador = Confere(adminKey!);
    if (!administrador && (context.Request.Path.StartsWithSegments("/api/gestao") || !Confere(sellerKey!)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { erro = "Chave de acesso inválida para esta operação." });
        return;
    }

    try { await next(context); }
    catch (RegraException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { erro = ex.Message });
    }
});

app.MapGet("/api/catalogo", (Registro db) => db.Ler(dados => new
{
    baixadoEm = DateTimeOffset.UtcNow,
    produtos = dados.Produtos,
    clientes = dados.Clientes.Select(c => new { c.Id, c.Nome }).ToArray()
}));
app.MapPost("/api/pedidos", (EntradaPedido entrada, Registro db) =>
    db.Gravar(dados => Operacao.Receber(dados, entrada, DateTimeOffset.UtcNow)));
app.MapGet("/api/gestao", (Registro db) => db.Ler(dados => dados));
app.MapPost("/api/gestao/produtos", (NovoProduto entrada, Registro db) => db.Gravar(dados =>
{
    if (string.IsNullOrWhiteSpace(entrada.Nome) || entrada.Preco is <= 0 or > 100000
        || decimal.Round(entrada.Preco, 2) != entrada.Preco || entrada.ValidadeMinima is < 0 or > 365)
        throw new RegraException("Informe um nome, preço com até duas casas decimais e validade mínima entre 0 e 365 dias.");
    var produto = new Produto(Guid.NewGuid(), entrada.Nome.Trim(), entrada.Preco, entrada.ValidadeMinima);
    dados.Produtos.Add(produto);
    return produto;
}));
app.MapPost("/api/gestao/clientes", (NovoCliente entrada, Registro db) => db.Gravar(dados =>
{
    if (string.IsNullOrWhiteSpace(entrada.Nome) || entrada.Desde > Operacao.Hoje(DateTimeOffset.UtcNow))
        throw new RegraException("Informe o nome e uma data de início válida.");
    var cliente = new Cliente(Guid.NewGuid(), entrada.Nome.Trim(), entrada.Desde);
    dados.Clientes.Add(cliente);
    return cliente;
}));
app.MapPost("/api/gestao/lotes", (NovoLote entrada, Registro db) => db.Gravar(dados =>
{
    if (!dados.Produtos.Any(p => p.Id == entrada.ProdutoId) || entrada.Quantidade is < 1 or > 1000000
        || entrada.Validade < Operacao.Hoje(DateTimeOffset.UtcNow))
        throw new RegraException("Confira o produto, a quantidade e a validade do lote.");
    var lote = new Lote(Guid.NewGuid(), entrada.ProdutoId, entrada.Validade, entrada.Quantidade, 0);
    dados.Lotes.Add(lote);
    return lote;
}));
app.MapPost("/api/gestao/expedicao/{id:guid}", (Guid id, Registro db) =>
    db.Gravar(dados => Operacao.Expedir(dados, id, DateTimeOffset.UtcNow)));
app.MapPost("/api/gestao/pagamentos/{id:guid}", (Guid id, EntradaPagamento entrada, Registro db) =>
    db.Gravar(dados => Operacao.Pagar(dados, id, entrada)));
if (app.Environment.IsDevelopment() && builder.Configuration["PANIFICADORA_DEMO"] == "1")
{
    app.MapPost("/api/gestao/demo/fechar", (Registro db) => db.Gravar(dados =>
        Operacao.Fechar(dados, Operacao.Hoje(DateTimeOffset.UtcNow).AddDays(1))));
}

app.Run();

sealed class Registro
{
    private readonly object _trava = new();
    private readonly string _arquivo;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Registro(IConfiguration config)
    {
        _arquivo = Path.GetFullPath(config["PANIFICADORA_DADOS"] ?? "dados/operacao.json");
    }

    private Dados Abrir() => File.Exists(_arquivo)
        ? JsonSerializer.Deserialize<Dados>(File.ReadAllText(_arquivo), _json)
            ?? throw new InvalidDataException("Arquivo de operação inválido.")
        : new Dados();

    public T Ler<T>(Func<Dados, T> consultar)
    {
        lock (_trava) return consultar(Abrir());
    }

    public T Gravar<T>(Func<Dados, T> alterar)
    {
        lock (_trava)
        {
            var dados = Abrir();
            var resultado = alterar(dados);
            Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
            var temporario = _arquivo + ".tmp";
            using (var arquivo = File.Create(temporario))
            {
                JsonSerializer.Serialize(arquivo, dados, _json);
                arquivo.Flush(flushToDisk: true);
            }
            File.Move(temporario, _arquivo, overwrite: true);
            return resultado;
        }
    }
}

sealed class FechamentoAutomatico(Registro db, ILogger<FechamentoAutomatico> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                var ultimo = Operacao.DiaComercial(DateTimeOffset.UtcNow).AddDays(-1);
                if (db.Ler(d => d.Pedidos.Any(p => p.Status == "Recebido" && p.Dia <= ultimo)))
                    db.Gravar(d => Operacao.Fechar(d, ultimo));
            }
            catch (Exception ex) { logger.LogError(ex, "Falha ao fechar pedidos. A operação será tentada novamente."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
