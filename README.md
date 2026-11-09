# Panificadora do Rafael

Projeto acadêmico individual de Rafael ([rafaelgitwork](https://github.com/rafaelgitwork)).

Sistema B2B para uma distribuidora de insumos de panificação. O vendedor consulta a carteira, segue o roteiro e registra clientes e pedidos em campo. A distribuidora decide as quantidades a liberar, controla lotes, registra o faturamento e acompanha os recebimentos.

## Estrutura

| Projeto | Tecnologia | Responsabilidade |
|---|---|---|
| Panificadora.App | .NET MAUI 10 e SQLite | Carteira, roteiro, histórico, catálogo e fila de clientes e pedidos offline. |
| Panificadora.Web | Blazor 10, Interactive Server | Cadastros, estoque, revisão de pedidos, corte, faturamento, expedição e financeiro. |
| Panificadora.Api | ASP.NET Core 10, EF Core 10 e MySQL | Autenticação, regras de negócio, migrações e persistência central. |
| Panificadora.Contratos | .NET 10 | Contratos JSON e cliente HTTP compartilhados. |
| tests | xUnit | Cálculos financeiros e integração HTTP com MySQL. |

A solução `PanificadoraDoRafael.slnx` reúne os projetos. O app e o site acessam os dados centrais pela API.

## Executar no Windows

Use o SDK .NET 10, a carga de trabalho MAUI e uma instância MySQL. Crie o banco `panificadora` e um usuário com permissões de leitura, escrita e migração nesse banco.

1. **API.** Configure as variáveis abaixo em um terminal PowerShell e inicie o servidor. Os valores de e-mail e senha são usados no primeiro acesso à gestão. A senha precisa ter pelo menos 12 caracteres. Gere uma nova chave e mantenha as credenciais fora do repositório.

   ```powershell
   $env:ConnectionStrings__Distribuidora = 'Server=localhost;Port=3306;Database=panificadora;User ID=panificadora;Password=SUA_SENHA_MYSQL'
   $env:Autenticacao__Chave = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
   $env:Gestao__Email = 'gestao@panificadora.local'
   $env:Gestao__Senha = 'TROQUE_POR_UMA_SENHA_FORTE'
   $env:Banco__Migrar = 'true'
   dotnet run --project Panificadora.Api --no-launch-profile --urls http://localhost:5100
   ```

2. **Site Blazor.** Em outro terminal, inicie o site e abra http://localhost:5200. Entre com a conta de gestão e cadastre um vendedor ativo. O site usa a API local por padrão. A variável `Api__Url` permite definir outro endereço.

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   dotnet run --project Panificadora.Web --no-launch-profile --urls http://localhost:5200
   ```

3. **Aplicativo MAUI.** Execute pelo Visual Studio com o destino Windows Machine ou use o comando abaixo. Entre com o vendedor criado pelo site e use a URL local padrão. Para um aparelho Android ou outro cliente fora do computador da API, informe uma URL HTTPS acessível ao aparelho.

   ```powershell
   dotnet build Panificadora.App -t:Run -f net10.0-windows10.0.19041.0
   ```

Para testar no emulador Android com a API rodando no mesmo computador, use `adb reverse tcp:5100 tcp:5100` e informe `http://localhost:5100/` na tela de acesso. Para acessar a API em outro computador, use HTTPS.

## Testes

```powershell
dotnet test tests/Panificadora.UnitTests
$env:PANIFICADORA_TEST_MYSQL = 'Server=localhost;Port=3306;User ID=USUARIO_DE_TESTE;Password=SENHA_DE_TESTE'
dotnet test tests/Panificadora.IntegrationTests
```

Use uma instância de testes. O teste de integração cria um banco temporário com nome aleatório, aplica as migrações e remove esse banco ao terminar. A conta de teste precisa de permissão para essas operações.

## Entrega

- [Especificação do sistema](docs/ESPECIFICACAO.md)
- [Roteiro de demonstração e critérios de aceite](docs/DEMONSTRACAO.md)
