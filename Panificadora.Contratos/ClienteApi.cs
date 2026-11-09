using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Panificadora.Contratos;

public sealed class ClienteApi(HttpClient http)
{
    public string? Token { get; set; }

    public Task<T> Obter<T>(string caminho) => Enviar<T>(HttpMethod.Get, caminho, null);
    public Task<T> Postar<T>(string caminho, object corpo) => Enviar<T>(HttpMethod.Post, caminho, corpo);

    private async Task<T> Enviar<T>(HttpMethod metodo, string caminho, object? corpo)
    {
        using var requisicao = new HttpRequestMessage(metodo, "api/" + caminho);
        if (Token is not null)
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (corpo is not null)
            requisicao.Content = JsonContent.Create(corpo);
        using var resposta = await http.SendAsync(requisicao);
        if (!resposta.IsSuccessStatusCode)
        {
            var erro = (int)resposta.StatusCode < 500 ? await resposta.Content.ReadAsStringAsync() : null;
            throw new HttpRequestException(string.IsNullOrWhiteSpace(erro)
                ? $"Operação recusada ({(int)resposta.StatusCode})." : erro, null, resposta.StatusCode);
        }
        return await resposta.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("Resposta vazia.");
    }
}
