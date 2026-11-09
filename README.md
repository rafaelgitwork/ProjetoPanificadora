# Panificadora do Rafael

Projeto acadêmico individual para uma distribuidora de produtos de panificação. A equipe registra pedidos no navegador, mesmo sem conexão depois da primeira carga, e acompanha o fechamento, o estoque, a expedição e o contas a receber pelo painel.

## Executar no Windows

Instale o SDK .NET 10 e, na pasta do projeto, rode no PowerShell:

```powershell
$env:PANIFICADORA_ADMIN = [guid]::NewGuid().ToString('N')
$env:PANIFICADORA_VENDAS = [guid]::NewGuid().ToString('N')
Write-Host "Gestão: $env:PANIFICADORA_ADMIN"
Write-Host "Vendas: $env:PANIFICADORA_VENDAS"
dotnet run --no-launch-profile --urls http://localhost:5167
```

Abra http://localhost:5167. A chave de gestão libera cadastros e operações administrativas. A chave de vendas libera o catálogo e o envio de pedidos. O arquivo local de dados é criado em `dados/operacao.json`. Use uma única instância do servidor para cada arquivo.

Para habilitar o fechamento manual de demonstração, defina `ASPNETCORE_ENVIRONMENT=Development` e `PANIFICADORA_DEMO=1` antes de iniciar o servidor e abra http://localhost:5167/?demo. Use dados de teste nesse modo, pois o botão antecipa o fechamento.

## Demonstrar o fluxo

1. Entre com a chave de gestão. Cadastre dois clientes, um produto e dois lotes do mesmo produto com validades diferentes.
2. Em outra janela, entre com a chave de vendas e aguarde o carregamento do catálogo. Em localhost ou HTTPS, o navegador pode manter a interface disponível offline após a primeira visita.
3. Desative a conexão pelo painel de rede do navegador. Selecione um cliente, adicione itens e salve o pedido. Repita para o segundo cliente com demanda total acima do estoque.
4. Restabeleça a conexão e transmita a fila. No painel de gestão, atualize os dados e aguarde o corte ou use o botão de demonstração.
5. Confira os itens atendidos, os pesos do rateio e a reserva dos lotes. Confirme a saída física e registre um pagamento parcial, seguido da quitação.

## Regras implementadas

- O servidor atribui a data comercial no recebimento. A partir das 22h de São Paulo, o pedido entra no próximo dia. O serviço também busca dias anteriores ainda pendentes quando volta a rodar.
- Cada pedido tem um identificador gerado no navegador. O mesmo pedido pode ser reenviado sem duplicação. O preço informado precisa bater com o catálogo, e a API rejeita a data de catálogo quando tem mais de 48 horas.
- O fechamento limita o atendimento ao estoque elegível. Se houver escassez, distribui as unidades em proporção à demanda e ao peso do cliente, considerando tempo de relacionamento e saldo vencido. Em empates usa o identificador do pedido como critério estável.
- A reserva consome primeiro o lote elegível com vencimento mais próximo. A baixa física ocorre na expedição, que pode ser chamada novamente sem duplicar a movimentação.
- A venda gera até três parcelas em intervalos de 30 dias. Vencimentos em fim de semana passam para segunda-feira. Pagamentos são aplicados às parcelas abertas mais antigas e os lançamentos ficam registrados.

## Verificação

```powershell
pwsh -File verificacao/fluxo.ps1
```

O roteiro inicia um servidor local com arquivo temporário e verifica permissões, reenvio, dia comercial, fechamento, rateio, reserva FEFO, saída física e quitação. Use `-Porta` se a porta 5167 já estiver em uso.

## Escopo desta versão

A aplicação usa ASP.NET Core, uma interface web responsiva, armazenamento local no navegador e um arquivo JSON central. O modo offline é para pedidos e catálogo. Esta versão ainda não inclui contas individuais e aprovação de vendedores, carteiras e roteiros, promoções, juros e descontos, realocação automática de lotes ou banco relacional. O roteiro automatizado testa a API. O uso offline no navegador tem um roteiro manual acima.
