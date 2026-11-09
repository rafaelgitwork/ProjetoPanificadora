# Especificação da Panificadora do Rafael

## Objetivo e componentes

A distribuidora vende fermentos, farinhas, misturas para bolos e salgados e margarina em baldes de 20 L a outras empresas. A solução usa três aplicações separadas. O aplicativo nativo é feito em .NET MAUI e usa SQLite para trabalhar offline. O site administrativo usa Blazor Interactive Server. A API ASP.NET Core concentra as regras e grava os dados em MySQL com EF Core. Os clientes consomem contratos HTTP/JSON e não se conectam ao MySQL.

## Cadastros e permissões

- Estados e cidades identificam as localidades. Um território é um trecho identificado dentro de uma cidade e possui um vendedor responsável.
- Vendedor, cliente e produto possuem status ativo ou inativo. Vendedores têm credenciais individuais. A API confere o status do vendedor mesmo quando seu token ainda está válido.
- Cada cliente pertence a um território e tem identificação fiscal, e-mail, telefone e endereço. A gestão monta o roteiro com dia da semana e sequência inteira. É possível inserir a posição 15 entre 10 e 20.
- Produtos têm grupo, unidade de venda, preço, conservação, validade mínima e lançamento. O catálogo destaca novidade e promoção vigente. A quantidade física fica na gestão.
- A gestão cadastra formas de pagamento, como Pix ou boleto, e condições, que definem percentuais, prazos e taxas.

## Operação do vendedor

O vendedor baixa preços, carteira e roteiro. Pode abrir a ficha pelo roteiro do dia ou pela lista de clientes, consultar a última compra por produto e ver principal em aberto e vencido. Clientes e pedidos novos podem ser cadastrados offline e aguardam na fila SQLite da conta. O envio confirma o cadastro antes do pedido vinculado e usa identificadores estáveis para evitar duplicações.

O aplicativo aberto e conectado tenta transmitir nas janelas que começam às 12h e 19h em São Paulo. Cada janela dura 15 minutos e permite novas tentativas quando há falha. O botão manual continua disponível. Ao salvar um pedido, ele entra na fila. Registros recusados continuam na fila com o motivo. Se o preço do catálogo mudou, o vendedor pode atualizar os dados e revisar o pedido retido.

## Operação da distribuidora

A gestão consulta os pedidos dos vendedores e informa o que pode liberar em cada item. A API verifica o limite solicitado, valida o saldo elegível e reserva primeiro os lotes de vencimento mais próximo. A confirmação é transacional. Uma liberação maior que o saldo é recusada sem gravação parcial.

O sistema conserva as quantidades solicitada e liberada. Somente a quantidade liberada integra os itens e o valor da venda. A gestão registra a nota emitida, confirma a saída e a venda passa a Em rota. O sistema registra o número da nota emitida externamente. A emissão de NF-e depende de uma integração fiscal fora desta versão.

## Contas a receber

Cada condição define dias e percentual para cada parcela, além de multa, juros diários e desconto diário por antecipação. A soma dos percentuais deve ser 100%. Uma condição à vista usa prazo zero. Cada parcela tem sua própria forma de pagamento e fica vinculada à venda, com número, valor e vencimento. Quando a nota é registrada, o painel mostra a identificação no formato número da nota/parcela.

Ao registrar uma baixa, o sistema considera multa e juros de atraso sobre o saldo e permite desconto na quitação antecipada. Pagamentos parciais reduzem o principal após a apropriação dos encargos. Os valores são registrados em centavos e os títulos somam o total vendido. A dívida do cliente é informativa e não bloqueia pedidos por limite de crédito.

## IA e configurações

O desenvolvimento usou OpenAI Codex no aplicativo desktop e SDK .NET 10. Os parâmetros de execução, incluindo conexão MySQL e credenciais de gestão, estão descritos no [README](../README.md). A solução adota separação entre aplicativo, site e API, contratos HTTP/JSON, autenticação com JWT, migrações versionadas e testes unitários e de integração. Os demais padrões ensinados pelo professor Luciano dependem do material da disciplina.
