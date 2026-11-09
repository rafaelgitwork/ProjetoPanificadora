param([int]$Porta = 5167)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$base = "http://127.0.0.1:$Porta"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:PANIFICADORA_DEMO = '1'
$env:PANIFICADORA_ADMIN = [guid]::NewGuid().ToString('N')
$env:PANIFICADORA_VENDAS = [guid]::NewGuid().ToString('N')
$env:PANIFICADORA_DADOS = Join-Path ([IO.Path]::GetTempPath()) ("panificadora-" + [guid]::NewGuid() + ".json")

function Confirmar($condicao, $mensagem) {
    if (!$condicao) { throw $mensagem }
}

function Api($rota, $corpo, [switch]$Vendedor) {
    $chave = if ($Vendedor) { $env:PANIFICADORA_VENDAS } else { $env:PANIFICADORA_ADMIN }
    $parametros = @{ Uri = "$base/api/$rota"; Headers = @{ 'X-Acesso' = $chave } }
    if ($null -ne $corpo) {
        $parametros.Method = 'Post'
        $parametros.ContentType = 'application/json; charset=utf-8'
        $parametros.Body = $corpo | ConvertTo-Json -Depth 12 -Compress
    }
    Invoke-RestMethod @parametros
}

dotnet build "$raiz/PanificadoraDoRafael.csproj" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
$dll = Join-Path $raiz 'bin/Debug/net10.0/PanificadoraDoRafael.dll'
$servidor = Start-Process dotnet -ArgumentList ('"' + $dll + '" --urls ' + $base) -WorkingDirectory $raiz -WindowStyle Hidden -PassThru

try {
    $pronto = $false
    for ($i = 0; $i -lt 30; $i++) {
        if ($servidor.HasExited) { throw 'O servidor encerrou antes de ficar disponível.' }
        try { $null = Api catalogo; $pronto = $true; break }
        catch { if ($i -eq 29) { throw }; Start-Sleep -Milliseconds 350 }
    }
    Confirmar $pronto 'A API não ficou disponível.'
    $pagina = Invoke-WebRequest -Uri $base -UseBasicParsing
    Confirmar ($pagina.Content -match 'Panificadora do Rafael') 'A página inicial não foi servida.'
    try {
        $null = Api gestao -Vendedor
        throw 'A chave do vendedor permitiu acesso à gestão.'
    } catch {
        if ($_.Exception.Response.StatusCode -ne 401) { throw }
    }

    $produto = Api gestao/produtos @{ nome = 'Farinha especial'; preco = 12.50; validadeMinima = 2 }
    $fuso = [TimeZoneInfo]::FindSystemTimeZoneById('America/Sao_Paulo')
    $hoje = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $fuso)
    $antigo = Api gestao/clientes @{ nome = 'Padaria da Praça'; desde = $hoje.AddYears(-6).ToString('yyyy-MM-dd') }
    $novo = Api gestao/clientes @{ nome = 'Pão da Vila'; desde = $hoje.ToString('yyyy-MM-dd') }
    $primeiro = Api gestao/lotes @{ produtoId = $produto.id; quantidade = 4; validade = $hoje.AddDays(10).ToString('yyyy-MM-dd') }
    $segundo = Api gestao/lotes @{ produtoId = $produto.id; quantidade = 3; validade = $hoje.AddDays(20).ToString('yyyy-MM-dd') }
    $catalogo = Api catalogo -Vendedor
    $pedidos = foreach ($cliente in @($antigo, $novo)) {
        $entrada = @{
            id = [guid]::NewGuid(); clienteId = $cliente.id; catalogoEm = $catalogo.baixadoEm; parcelas = 2
            itens = @(@{ produtoId = $produto.id; quantidade = 8; preco = 12.50 })
        }
        $pedido = Api pedidos $entrada -Vendedor
        $reenvio = Api pedidos $entrada -Vendedor
        Confirmar ($pedido.id -eq $reenvio.id) 'O pedido foi duplicado no reenvio.'
        $diaEsperado = $hoje.AddDays([int]($hoje.Hour -ge 22)).ToString('yyyy-MM-dd')
        Confirmar ($pedido.dia -eq $diaEsperado) 'O dia comercial não respeitou o corte das 22h.'
        $pedido
    }
    Confirmar ((Api gestao/demo/fechar @{}) -eq 2) 'O fechamento não processou os dois pedidos.'
    Confirmar ((Api gestao/demo/fechar @{}) -eq 0) 'O fechamento duplicou pedidos.'
    $estado = Api gestao
    Confirmar ($estado.pedidos.Count -eq 2 -and $estado.vendas.Count -eq 2) 'Quantidade incorreta de pedidos ou vendas.'
    Confirmar (($estado.rateios.atendido | Measure-Object -Sum).Sum -eq 7) 'O rateio não esgotou o saldo elegível.'
    $venda = $estado.vendas | Where-Object clienteId -eq $antigo.id
    Confirmar ($venda.total -eq 50 -and $venda.parcelas.Count -eq 2) 'O peso ou o parcelamento divergiu.'
    Confirmar (($estado.lotes | Where-Object id -eq $primeiro.id).reservado -eq 4) 'O lote de vencimento mais próximo não foi priorizado.'
    foreach ($item in $estado.vendas) {
        $saida = Api "gestao/expedicao/$($item.id)" @{}
        $reenvio = Api "gestao/expedicao/$($item.id)" @{}
        Confirmar ($saida.status -eq 'Em rota' -and $reenvio.status -eq 'Em rota') 'A expedição não foi idempotente.'
    }
    $estoque = (Api gestao).lotes
    Confirmar (($estoque.fisico | Measure-Object -Sum).Sum -eq 0) 'A baixa física divergiu.'
    $parcial = @{ id = [guid]::NewGuid(); valor = 15 }
    $null = Api "gestao/pagamentos/$($venda.id)" $parcial
    $null = Api "gestao/pagamentos/$($venda.id)" $parcial
    $null = Api "gestao/pagamentos/$($venda.id)" @{ id = [guid]::NewGuid(); valor = 35 }
    $estado = Api gestao
    $venda = $estado.vendas | Where-Object clienteId -eq $antigo.id
    Confirmar ($estado.pagamentos.Count -eq 2 -and ($venda.parcelas.pago | Measure-Object -Sum).Sum -eq 50) 'O pagamento não conciliou.'
    Write-Output 'Fluxo aprovado: acesso, pedido, reenvio, corte, rateio, FEFO, expedição, parcelas e quitação.'
}
finally {
    if (!$servidor.HasExited) { Stop-Process -Id $servidor.Id }
    Remove-Item -LiteralPath $env:PANIFICADORA_DADOS -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:PANIFICADORA_DEMO, Env:PANIFICADORA_ADMIN, Env:PANIFICADORA_VENDAS, Env:PANIFICADORA_DADOS
}
