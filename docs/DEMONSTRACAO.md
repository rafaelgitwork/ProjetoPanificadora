# Demonstração e critérios de aceite

## Demonstrar a versão atual

Siga a configuração no [README](../README.md). Para a demonstração, habilite o modo de desenvolvimento e o botão de fechamento.

1. Com a chave de gestão, cadastre dois clientes, um produto e dois lotes do mesmo produto, com validades diferentes.
2. Com a chave de vendas, carregue o catálogo. Aguarde o registro do service worker, desconecte o navegador e salve um pedido para cada cliente. Recarregue a página offline e confira se a fila permanece. Reconecte e verifique o envio. Este é um teste manual do navegador.
3. Solicite mais unidades do que o estoque comporta. Na gestão, feche o lote em modo de demonstração. Confira o que cada cliente solicitou e recebeu, o total da venda e a reserva por validade.
4. Confirme a saída. Confira que o saldo físico diminuiu e que a venda passou para Em rota.
5. Registre um pagamento parcial e depois quite o saldo. Confira os valores por parcela.

## Verificação automatizada da API

Execute no PowerShell 7, a partir da raiz:

```powershell
pwsh -File verificacao/fluxo.ps1
```

A verificação cobre acesso, reenvio, dia comercial no horário do teste, fechamento, rateio em escassez, reserva por validade, baixa idempotente e quitação. O fluxo automatizado usa dados temporários e não opera o navegador.

## Cenários de aceite do enunciado

| Cenário | Passos e resultado esperado | Estado |
|---|---|---|
| Inserção em roteiro | Cadastrar visitas 10, 20 e 30. Inserir a 15 e consultar o roteiro do dia. O app deve listar 10, 15, 20 e 30. | Pendente. |
| Cadastro em campo | Sem conexão, cadastrar uma panificadora e criar um pedido para ela. Sincronizar e repetir o envio. Deve existir um único cliente e um único pedido. | Pendente. |
| Histórico e dívida | Após uma semana sem compra, a ficha deve mostrar a última compra efetiva por produto, com quantidade e preço. Deve informar saldo em aberto e vencido. | Pendente. |
| Envio por janela | Criar pedidos pela manhã e à tarde. A fila deve enviar às 12h e 19h quando possível, manter os itens que falharem e aceitar envio manual. | Envio manual disponível. Janelas pendentes. |
| Corte de 15 unidades | Receber pedidos de 5, 8 e 9 unidades. A gestão libera 2, 6 e 7. A API confirma 15 unidades ao todo e recusa liberar uma 16ª. A venda cobra apenas as quantidades liberadas. | O estoque e o rateio atual têm verificação. O corte manual exato está pendente. |
| Condição de R$ 235,00 | Configurar 50% em 7 dias por Pix e 50% em 14 dias por boleto. Para venda de 19/10/2026, gerar R$ 117,50 em 26/10/2026 e R$ 117,50 em 02/11/2026. Se vinculadas à nota 123, as parcelas devem aparecer como 123/1 e 123/2. | Pendente. A versão atual usa prazos fixos de 30 dias. |
| Vencimentos com percentuais distintos | Configurar 33,33%, 33,33% e 33,34% para 7, 14 e 21 dias. Verificar que a soma dos títulos em centavos equivale ao total da venda. | Pendente. |
