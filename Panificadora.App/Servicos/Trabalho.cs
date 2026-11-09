using System.Net;
using System.Text.Json;
using Panificadora.App.Dados;
using Panificadora.Contratos;

namespace Panificadora.App.Servicos;

public sealed class Trabalho(BaseLocal banco)
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static DateOnly Hoje => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime);
    public static long? InicioDaJanela
    {
        get
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso);
            if (local.Hour is not (12 or 19) || local.Minute >= 15) return null;
            return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).ToUnixTimeSeconds();
        }
    }

    private const string ChaveSessao = "panificadora.sessao";
    private ClienteApi? api;
    private Contexto? contexto;
    private HttpClient? http;
    private string Conta => contexto is not null ? $"{contexto.Endereco}|{contexto.Sessao.Id}" :
        throw new InvalidOperationException("Entre com a conta de vendas.");

    public Sessao? Sessao => contexto?.Sessao;
    public string? Endereco => contexto?.Endereco;
    public DadosVendedor? Dados => contexto is null ? null : banco.Catalogo(Conta);
    public Envio[] Fila => contexto is null ? [] : banco.Fila(Conta);

    public async Task Restaurar()
    {
        var json = await SecureStorage.Default.GetAsync(ChaveSessao);
        if (json is null) return;
        contexto = JsonSerializer.Deserialize<Contexto>(json);
        if (contexto is not null) Configurar(contexto.Endereco, contexto.Sessao.Token);
    }

    public async Task Entrar(string endereco, string email, string senha)
    {
        if (!Uri.TryCreate(endereco.TrimEnd('/') + "/", UriKind.Absolute, out var url) ||
            (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)))
            throw new InvalidOperationException("Use HTTPS ou o endereço HTTP local de desenvolvimento.");
        var nova = new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(15) };
        var cliente = new ClienteApi(nova);
        try
        {
            var sessao = await cliente.Postar<Sessao>("sessao", new Acesso(email, senha));
            if (sessao.Perfil != "Vendas") throw new InvalidOperationException("Use uma conta de vendedor.");
            var atual = new Contexto(url.AbsoluteUri, sessao);
            await SecureStorage.Default.SetAsync(ChaveSessao, JsonSerializer.Serialize(atual));
            http?.Dispose();
            http = nova;
            contexto = atual;
            api = cliente;
            api.Token = sessao.Token;
        }
        catch { nova.Dispose(); throw; }
    }

    private void Configurar(string endereco, string token)
    {
        http?.Dispose();
        http = new HttpClient { BaseAddress = new Uri(endereco), Timeout = TimeSpan.FromSeconds(15) };
        api = new ClienteApi(http) { Token = token };
    }

    public void Sair()
    {
        contexto = null;
        api = null;
        http?.Dispose();
        http = null;
        SecureStorage.Default.Remove(ChaveSessao);
    }

    public async Task Baixar()
    {
        var dados = await Api.Obter<DadosVendedor>("campo/dados");
        banco.SalvarCatalogo(Conta, dados);
    }

    public void Guardar(Cliente cliente) => banco.Enfileirar(Conta, cliente.Id, "clientes", cliente);
    public void Guardar(NovoPedido pedido) => banco.Enfileirar(Conta, pedido.Id, "pedidos", pedido);

    public void Revisar(NovoPedido pedido) => banco.RevisarPedido(Conta, pedido);

    public Cliente[] Carteira() => (Dados?.Clientes ?? []).Concat(Fila.Where(x => x.Tipo == "clientes")
        .Select(x => JsonSerializer.Deserialize<Cliente>(x.Corpo)!)).DistinctBy(c => c.Id).OrderBy(c => c.Nome).ToArray();

    public async Task<string> Enviar(long? limite = null)
    {
        var aceitos = 0;
        var falharam = 0;
        foreach (var envio in Fila.Where(x => limite is null || x.CriadoEm < limite).OrderBy(x => x.Tipo == "clientes" ? 0 : 1))
        {
            try
            {
                await Api.Postar<JsonElement>("campo/" + envio.Tipo, JsonSerializer.Deserialize<JsonElement>(envio.Corpo));
                banco.Confirmar(Conta, envio.Id);
                aceitos++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            {
                banco.RegistrarErro(Conta, envio.Id, ex.Message);
                falharam++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return $"{aceitos} confirmado(s). Restam {Fila.Length}. {ex.Message}";
            }
        }
        return $"{aceitos} confirmado(s). {falharam} retido(s).";
    }

    private ClienteApi Api => api ?? throw new InvalidOperationException("Entre com a conta de vendas.");
    public sealed record Contexto(string Endereco, Sessao Sessao);
}
