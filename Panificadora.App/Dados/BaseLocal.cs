using System.Text.Json;
using Microsoft.Data.Sqlite;
using Panificadora.Contratos;

namespace Panificadora.App.Dados;

public sealed record Envio(Guid Id, string Tipo, string Corpo, string? Erro, long CriadoEm);

public sealed class BaseLocal
{
    private readonly string conexao = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(FileSystem.AppDataDirectory, "vendas.db")
    }.ToString();

    private SqliteConnection Abrir()
    {
        var banco = new SqliteConnection(conexao);
        banco.Open();
        using var comando = banco.CreateCommand();
        comando.CommandText = """
            CREATE TABLE IF NOT EXISTS catalogo (conta TEXT PRIMARY KEY, dados TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS fila (
                ordem INTEGER PRIMARY KEY AUTOINCREMENT,
                conta TEXT NOT NULL, id TEXT NOT NULL, tipo TEXT NOT NULL, corpo TEXT NOT NULL, erro TEXT, criadoEm INTEGER NOT NULL,
                UNIQUE(conta, id)
            );
            """;
        comando.ExecuteNonQuery();
        return banco;
    }

    public DadosVendedor? Catalogo(string conta)
    {
        using var banco = Abrir();
        using var comando = banco.CreateCommand();
        comando.CommandText = "SELECT dados FROM catalogo WHERE conta = @conta";
        comando.Parameters.AddWithValue("@conta", conta);
        return comando.ExecuteScalar() is string dados ? JsonSerializer.Deserialize<DadosVendedor>(dados) : null;
    }

    public void SalvarCatalogo(string conta, DadosVendedor dados) =>
        Executar("INSERT INTO catalogo VALUES (@conta, @dados) ON CONFLICT(conta) DO UPDATE SET dados = excluded.dados",
            ("@conta", conta), ("@dados", JsonSerializer.Serialize(dados)));

    public void Enfileirar(string conta, Guid id, string tipo, object corpo) =>
        Executar("INSERT INTO fila(conta, id, tipo, corpo, criadoEm) VALUES (@conta, @id, @tipo, @corpo, @criado)",
            ("@conta", conta), ("@id", id.ToString()), ("@tipo", tipo), ("@corpo", JsonSerializer.Serialize(corpo)), ("@criado", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public void RevisarPedido(string conta, NovoPedido pedido) =>
        Executar("UPDATE fila SET corpo = @corpo, erro = NULL WHERE conta = @conta AND id = @id AND tipo = 'pedidos' AND erro IS NOT NULL",
            ("@corpo", JsonSerializer.Serialize(pedido)), ("@conta", conta), ("@id", pedido.Id.ToString()));

    public Envio[] Fila(string conta)
    {
        using var banco = Abrir();
        using var comando = banco.CreateCommand();
        comando.CommandText = "SELECT id, tipo, corpo, erro, criadoEm FROM fila WHERE conta = @conta ORDER BY ordem";
        comando.Parameters.AddWithValue("@conta", conta);
        using var leitura = comando.ExecuteReader();
        var fila = new List<Envio>();
        while (leitura.Read())
            fila.Add(new(Guid.Parse(leitura.GetString(0)), leitura.GetString(1), leitura.GetString(2),
                leitura.IsDBNull(3) ? null : leitura.GetString(3), leitura.GetInt64(4)));
        return fila.ToArray();
    }

    public void Confirmar(string conta, Guid id) =>
        Executar("DELETE FROM fila WHERE conta = @conta AND id = @id", ("@conta", conta), ("@id", id.ToString()));

    public void RegistrarErro(string conta, Guid id, string erro) =>
        Executar("UPDATE fila SET erro = @erro WHERE conta = @conta AND id = @id",
            ("@erro", erro), ("@conta", conta), ("@id", id.ToString()));

    private void Executar(string sql, params (string Nome, object Valor)[] parametros)
    {
        using var banco = Abrir();
        using var comando = banco.CreateCommand();
        comando.CommandText = sql;
        foreach (var (nome, valor) in parametros) comando.Parameters.AddWithValue(nome, valor);
        comando.ExecuteNonQuery();
    }
}
