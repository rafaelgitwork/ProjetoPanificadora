# Especificação da Panificadora do Rafael

## Objetivo

A distribuidora vende insumos de panificação para empresas. O vendedor consulta sua carteira e registra pedidos em campo. A distribuidora reúne esses pedidos, decide o que pode atender e controla estoque, faturamento, expedição e contas a receber. Os produtos incluem fermentos, farinhas, misturas para bolos e salgados e margarina em baldes de 20 L.

## Componentes e tecnologias

| Componente | Responsabilidade | Tecnologia |
|---|---|---|
| Aplicativo do vendedor | Carregar dados de trabalho, consultar o roteiro, cadastrar clientes e registrar pedidos offline. | Interface web responsiva em HTML, CSS e JavaScript. Service worker para a interface offline. Armazenamento local para catálogo e fila. |
| Site da distribuidora | Manter cadastros, organizar carteiras, revisar pedidos, confirmar vendas e acompanhar estoque e recebimentos. | Interface web servida pela aplicação ASP.NET Core. |
| API | Autenticar operações, sincronizar dados, validar estoque e processar vendas e recebimentos. | ASP.NET Core no .NET 10, HTTP e JSON. |
| Dados centrais | Registrar cadastros e transações. | A versão atual usa um arquivo JSON local, com escrita serializada para uma instância do servidor. |

O aplicativo e o painel compartilham a mesma aplicação web na versão atual. A possibilidade de entregar uma interface web como aplicativo do vendedor depende do critério de avaliação da disciplina.

## Cadastros e permissões

- **Estado e cidade.** Um estado tem sigla e nome. Cada cidade pertence a um estado e pode integrar um território comercial.
- **Vendedor e carteira.** Um vendedor possui identificação, contato e status ativo ou inativo. A gestão atribui territórios e clientes ao vendedor, impedindo carteiras ativas conflitantes.
- **Cliente.** O cadastro contém razão social, documento, endereço, cidade, e-mail, telefone e status. O vendedor pode cadastrar um cliente novo em campo. A sincronização deve cadastrar o cliente antes de enviar pedidos que dependem dele.
- **Roteiro.** A gestão define o dia, o trecho ou as cidades e os clientes na ordem de visita. A sequência aceita valores espaçados, como 10, 20 e 30, de modo que seja possível inserir uma visita na posição 15.
- **Produto e grupo.** Cada produto pertence a um grupo, possui preço, status e requisitos de conservação. O lote registra validade e quantidade. Produtos novos e promocionais recebem destaques visuais distintos.
- **Condição e forma de pagamento.** A condição define os prazos, percentuais e encargos. A forma define o meio de pagamento de cada parcela, como Pix ou boleto.

A API deve restringir o vendedor à própria carteira e manter o saldo exato de estoque acessível à gestão. A versão atual usa duas chaves compartilhadas, uma para gestão e outra para vendas.

## Fluxo do vendedor

1. Carregar os clientes da carteira, o roteiro e a lista de preços antes de sair.
2. Consultar o roteiro do dia. Na ficha de cada cliente, ver a última compra efetiva por produto, com data, quantidade e preço, e consultar o saldo em aberto e o saldo vencido.
3. Registrar clientes e pedidos mesmo sem conexão. Os pedidos permanecem em fila local.
4. Enviar a fila às 12h e às 19h, no horário de São Paulo, ou acionar o botão de envio. O vendedor deve ver o que foi confirmado e o que ainda aguarda nova tentativa. Cada registro local tem identificador único para reenvio seguro.
5. Consultar apenas o catálogo comercial disponível à venda, com os destaques de novidades e promoções.

A rotina de envio no navegador precisa definir o comportamento quando a aplicação estiver fechada ou o aparelho estiver sem rede no horário programado.

## Fluxo da distribuidora

1. Reunir os pedidos de todos os vendedores e exibir os itens e as quantidades solicitadas.
2. O responsável informa as quantidades liberadas por item. A interface pode exibir histórico de pagamento para apoiar a decisão.
3. Na confirmação, a API verifica se cada quantidade liberada cabe no pedido e no saldo elegível. Reserva primeiro os lotes aptos de vencimento mais próximo e impede estoque negativo.
4. Gerar a venda e seus itens pelas quantidades liberadas, preservando as quantidades originalmente pedidas. O cliente paga somente o valor atendido.
5. Registrar o faturamento e o vínculo com a nota fiscal. Confirmar a saída física no momento da expedição. A exigência de emissão integrada de NF-e precisa ser confirmada com o professor.

## Contas a receber

A condição pode prever pagamento à vista ou parcelas com prazos como 7, 14 e 21 dias. A soma dos percentuais deve ser 100%. A geração em centavos deve preservar o total da venda. Cada parcela registra número, venda ou nota, valor, data de vencimento e forma de pagamento.

Multa, juros de atraso e desconto por antecipação seguem a condição configurada. Os valores mencionados no enunciado são exemplos de configuração. A gestão registra pagamentos parciais ou integrais. A dívida é exibida ao vendedor e não bloqueia o pedido por limite de crédito.

## Estado da implementação

| Área | Implementado | Pendente |
|---|---|---|
| Vendas em campo | Catálogo local, pedido offline no navegador, transmissão manual e reenvio idempotente. | Vendedores individuais, carteiras, roteiros, cadastro em campo, última compra, dívida por cliente e envio às 12h e 19h. |
| Cadastros | Clientes, produtos, lotes e validade. | Estados, cidades, grupos, status, promoções e destaques de novidades. |
| Estoque e vendas | Fechamento automático às 22h, rateio por peso, reserva por validade, venda sobre o atendido e baixa idempotente. | Revisão e corte por item pela gestão, faturamento e vínculo com nota. |
| Financeiro | Parcelas em intervalos de 30 dias e pagamentos parciais ou totais. | Condições e formas configuráveis, prazos de 7 e 14 dias, identificação dos títulos e encargos. |

## IA, configurações e padrões

A ferramenta de IA usada no desenvolvimento e na documentação foi o **OpenAI Codex**, no aplicativo desktop. O projeto foi compilado com .NET SDK 10.0.403. As configurações de execução incluem chaves distintas de gestão e vendas, fuso America/Sao_Paulo e um modo de demonstração disponível em desenvolvimento. As chaves são fornecidas por variáveis de ambiente.

Os padrões específicos ensinados pelo professor Luciano não constam no enunciado recebido. A validação desses padrões com o material da disciplina permanece pendente.

Consulte o [roteiro de demonstração e critérios de aceite](DEMONSTRACAO.md).
