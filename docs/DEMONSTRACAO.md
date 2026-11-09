# Roteiro de demonstração

## Cadastros e operação em campo

1. Inicie API, site e app conforme o [README](../README.md).
2. No site, cadastre estado PR, cidade Foz do Iguaçu, um vendedor e um território. Cadastre três clientes e programe as posições 10, 20 e 15 para o dia da demonstração.
3. Cadastre um grupo e um produto de R$ 117,50 com lote de 15 unidades elegíveis. Para ver os destaques no catálogo, inclua também um produto recém-lançado e outro com promoção vigente.
4. Cadastre Pix e boleto e uma condição de duas parcelas de 50%, com Pix em 7 dias e boleto em 14 dias.
5. Entre no app com o vendedor e baixe preços, carteira e roteiro. Confira a ordem 10, 15 e 20. Desconecte o aparelho, registre um cliente e um pedido, feche e abra o app e confira se os registros permaneceram na fila. Reconecte e use o botão manual. O cadastro precisa chegar antes do pedido que depende dele.

## Corte, expedição e financeiro

1. Envie pedidos de 5, 8 e 9 unidades para três clientes. No painel, libere 2 e 6 unidades nos dois primeiros. Tente liberar 8 no terceiro. A API deve recusar. Libere 7 e confira o total de 15 unidades reservadas.
2. Abra a venda de 2 unidades. O total deve ser R$ 235,00. Registre a nota 123 e confirme a saída. Verifique o estoque físico e o status Em rota.
3. Confira os títulos 123/1 e 123/2 de R$ 117,50, um em Pix e outro em boleto. Para a venda de 19/10/2026 usada no teste, as datas esperadas são 26/10/2026 e 02/11/2026.
4. Registre um pagamento parcial e atualize a carteira do vendedor. Confira a dívida e a última compra, que deve mostrar as 2 unidades efetivamente vendidas.

O teste automatizado verifica o cenário de corte e o parcelamento contra MySQL. O roteiro também inclui a verificação manual de SQLite, interface e janelas de envio no dispositivo.
