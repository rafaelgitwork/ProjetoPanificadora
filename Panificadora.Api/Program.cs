using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Panificadora.Api.Comercial;
using Panificadora.Api.Dados;
using Panificadora.Api.Endpoints;
using Panificadora.Contratos;

var builder = WebApplication.CreateBuilder(args);
var conexao = builder.Configuration.GetConnectionString("Distribuidora")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Distribuidora.");
var segredo = builder.Configuration["Autenticacao:Chave"];
var emailGestao = builder.Configuration["Gestao:Email"]?.Trim().ToLowerInvariant();
var senhaGestao = builder.Configuration["Gestao:Senha"];
if (segredo is null || Encoding.UTF8.GetByteCount(segredo) < 32 ||
    string.IsNullOrWhiteSpace(emailGestao) || senhaGestao is null || senhaGestao.Length < 12)
    throw new InvalidOperationException("Configure a chave de autenticação e as credenciais de gestão.");

var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(segredo));
builder.Services.AddDbContext<ComercialDb>(options => options.UseMySQL(conexao));
builder.Services.AddScoped<Operacoes>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<PasswordHasher<Vendedor>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new()
    {
        ValidIssuer = "Panificadora.Api",
        ValidAudience = "Panificadora",
        IssuerSigningKey = chave,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new()
    {
        OnTokenValidated = async contexto =>
        {
            if (!contexto.Principal!.IsInRole("Vendas")) return;
            var db = contexto.HttpContext.RequestServices.GetRequiredService<ComercialDb>();
            var id = contexto.Principal.VendedorId();
            if (!await db.Vendedores.AnyAsync(v => v.Id == id && v.Ativo))
                contexto.Fail("Vendedor inativo.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => options.AddPolicy("login", contexto =>
    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new()
        {
            PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        })));

var app = builder.Build();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/sessao", async (Acesso entrada, ComercialDb db, PasswordHasher<Vendedor> hasher) =>
{
    var email = entrada.Email?.Trim().ToLowerInvariant();
    if (entrada.Senha is null || entrada.Senha.Length > 256) return Results.Unauthorized();
    var perfil = "Gestao";
    var id = Guid.Empty;
    var nome = "Distribuidora";
    if (email == emailGestao)
    {
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(entrada.Senha), Encoding.UTF8.GetBytes(senhaGestao)))
            return Results.Unauthorized();
    }
    else
    {
        var vendedor = await db.Vendedores.SingleOrDefaultAsync(v => v.Email == email && v.Ativo);
        if (vendedor is null) return Results.Unauthorized();
        var credencial = await db.Credenciais.FindAsync(vendedor.Id);
        if (credencial is null || hasher.VerifyHashedPassword(vendedor, credencial.SenhaHash, entrada.Senha) ==
            PasswordVerificationResult.Failed) return Results.Unauthorized();
        (perfil, id, nome) = ("Vendas", vendedor.Id, vendedor.Nome);
    }
    var token = new JwtSecurityToken("Panificadora.Api", "Panificadora",
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, perfil)],
        expires: DateTime.UtcNow.AddHours(16),
        signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new Sessao(new JwtSecurityTokenHandler().WriteToken(token), id, nome, perfil));
}).RequireRateLimiting("login");

app.MapGestao(emailGestao);
app.MapVendas();

if (builder.Configuration.GetValue("Banco:Migrar", false))
{
    await using var escopo = app.Services.CreateAsyncScope();
    await escopo.ServiceProvider.GetRequiredService<ComercialDb>().Database.MigrateAsync();
}
app.Run();

public partial class Program;

namespace Panificadora.Api.Endpoints
{
    internal static class Infra
    {
        public static Guid VendedorId(this ClaimsPrincipal usuario) =>
            Guid.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

        public static async Task<IResult> Alterar(this ComercialDb db, Func<Task<object>> acao)
        {
            await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var resultado = await acao();
                await db.SaveChangesAsync();
                await transacao.CommitAsync();
                return Results.Ok(resultado);
            }
            catch (RegraException erro) { return Results.BadRequest(erro.Message); }
            catch (DbUpdateException) { return Results.Conflict("O cadastro ou saldo mudou. Atualize e tente novamente."); }
            catch (MySql.Data.MySqlClient.MySqlException erro) when (erro.Number is 1205 or 1213)
            {
                return Results.Conflict("Outra operação está em andamento. Atualize e tente novamente.");
            }
        }
    }
}
