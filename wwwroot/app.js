const $ = id => document.getElementById(id);
const ler = (chave, padrao) => JSON.parse(localStorage.getItem(chave) ?? JSON.stringify(padrao));
const salvar = (chave, valor) => localStorage.setItem(chave, JSON.stringify(valor));
const moeda = valor => Number(valor).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const aviso = texto => { $('mensagem').textContent = texto; };
let chave = sessionStorage.getItem('panificadora.acesso') ?? '';
let catalogo = ler('panificadora.catalogo', null);
let fila = ler('panificadora.fila', []);
let itens = [];

async function api(rota, corpo) {
  const resposta = await fetch('/api' + rota, {
    method: corpo === undefined ? 'GET' : 'POST',
    headers: { 'X-Acesso': chave, 'Content-Type': 'application/json' },
    body: corpo === undefined ? undefined : JSON.stringify(corpo)
  });
  const resultado = await resposta.json().catch(() => ({}));
  if (!resposta.ok) {
    const erro = new Error(resultado.erro ?? 'A operação falhou (' + resposta.status + ').');
    erro.status = resposta.status;
    throw erro;
  }
  return resultado;
}

function opcoes(select, valores, rotulo) {
  const selecionado = select.value;
  select.replaceChildren(...valores.map(valor => new Option(rotulo(valor), valor.id)));
  if (valores.some(valor => valor.id === selecionado)) select.value = selecionado;
}

function atualizarTela() {
  $('catalogo').textContent = catalogo
    ? 'Catálogo baixado em ' + new Date(catalogo.baixadoEm).toLocaleString('pt-BR')
    : 'Entre com conexão para baixar o catálogo.';
  opcoes($('pedido').elements.cliente, catalogo?.clientes ?? [], c => c.nome);
  opcoes($('pedido').elements.produto, catalogo?.produtos ?? [], p => p.nome + ' · ' + moeda(p.preco));
  $('fila').textContent = fila.length + ' pedido(s) aguardando envio.';
  $('itens').replaceChildren(...itens.map((item, indice) => {
    const linha = document.createElement('li');
    linha.textContent = (catalogo?.produtos.find(p => p.id === item.produtoId)?.nome ?? 'Produto') + ' × ' + item.quantidade;
    linha.append(botao('Remover', () => { itens.splice(indice, 1); atualizarTela(); }));
    return linha;
  }));
}

function botao(rotulo, acao) {
  const elemento = document.createElement('button');
  elemento.type = 'button';
  elemento.textContent = rotulo;
  elemento.addEventListener('click', acao);
  return elemento;
}

async function executar(acao) {
  try { await acao(); }
  catch (erro) { aviso(erro.message); }
}

async function atualizar() {
  catalogo = await api('/catalogo');
  salvar('panificadora.catalogo', catalogo);
  atualizarTela();
  try {
    mostrarGestao(await api('/gestao'));
  } catch (erro) {
    $('gestao').hidden = true;
    if (erro.status !== 401) throw erro;
  }
}

function mostrarGestao(dados) {
  $('gestao').hidden = false;
  opcoes(document.querySelector('[name=produtoId]'), dados.produtos, p => p.nome);
  const nome = (lista, id) => lista.find(item => item.id === id)?.nome ?? id;
  const painel = $('operacao');
  painel.replaceChildren();
  function linha(texto, tag = 'p') {
    const elemento = document.createElement(tag);
    elemento.textContent = texto;
    painel.append(elemento);
    return elemento;
  }
  linha('Estoque por validade', 'h3');
  for (const lote of [...dados.lotes].sort((a, b) => a.validade.localeCompare(b.validade))) {
    linha(nome(dados.produtos, lote.produtoId) + ' · validade ' + lote.validade +
      ' · físico ' + lote.fisico + ' · reservado ' + lote.reservado);
  }
  linha('Pedidos e rateio', 'h3');
  for (const pedido of dados.pedidos) {
    linha(nome(dados.clientes, pedido.clienteId) + ' · ' + pedido.dia + ' · ' + pedido.status);
    for (const item of dados.rateios.filter(item => item.pedidoId === pedido.id)) {
      linha(nome(dados.produtos, item.produtoId) + ' · ' + item.atendido + '/' + item.solicitado +
        ' atendidos · peso ' + Number(item.peso).toFixed(2), 'small');
    }
  }
  linha('Vendas e parcelas', 'h3');
  for (const venda of dados.vendas) {
    const saldo = venda.parcelas.reduce((total, p) => total + Math.round((p.total - p.pago) * 100), 0) / 100;
    const artigo = linha(nome(dados.clientes, venda.clienteId) + ' · ' + venda.status +
      ' · total ' + moeda(venda.total) + ' · saldo ' + moeda(saldo), 'article');
    const parcelas = document.createElement('ul');
    for (const parcela of venda.parcelas) {
      const item = document.createElement('li');
      item.textContent = parcela.vencimento + ' · ' + moeda(parcela.total) + ' · pago ' + moeda(parcela.pago);
      parcelas.append(item);
    }
    artigo.append(parcelas);
    if (venda.status === 'Reservada') {
      artigo.append(botao('Confirmar saída', () => executar(async () => {
        await api('/gestao/expedicao/' + venda.id, {});
        await atualizar();
        aviso('Saída confirmada.');
      })));
    }
    if (saldo > 0) {
      const pagamento = document.createElement('form');
      const rotulo = document.createElement('label');
      rotulo.textContent = 'Valor a receber (R$)';
      const valor = document.createElement('input');
      Object.assign(valor, { type: 'number', min: '.01', max: String(saldo), step: '.01', required: true });
      const enviar = document.createElement('button');
      enviar.textContent = 'Registrar pagamento';
      rotulo.append(valor);
      pagamento.append(rotulo, enviar);
      let tentativa;
      pagamento.addEventListener('submit', evento => {
        evento.preventDefault();
        if (!tentativa || tentativa.valor !== valor.valueAsNumber)
          tentativa = { id: crypto.randomUUID(), valor: valor.valueAsNumber };
        enviar.disabled = true;
        executar(async () => {
          try {
            await api('/gestao/pagamentos/' + venda.id, tentativa);
            await atualizar();
            aviso('Pagamento registrado.');
          } finally { enviar.disabled = false; }
        });
      });
      artigo.append(pagamento);
    }
  }
}

$('acesso').addEventListener('submit', evento => {
  evento.preventDefault();
  $('gestao').hidden = true;
  chave = evento.currentTarget.elements.chave.value;
  executar(async () => {
    await atualizar();
    sessionStorage.setItem('panificadora.acesso', chave);
    evento.target.reset();
    aviso('Catálogo atualizado.');
  });
});

$('adicionar').addEventListener('click', () => executar(() => {
  const campos = $('pedido').elements;
  const produto = catalogo?.produtos.find(p => p.id === campos.produto.value);
  if (!produto || !campos.quantidade.reportValidity()) return;
  const existente = itens.find(item => item.produtoId === produto.id);
  const quantidade = (existente?.quantidade ?? 0) + campos.quantidade.valueAsNumber;
  if (quantidade > 10000) throw new Error('O limite por produto é de 10.000 unidades.');
  if (existente) existente.quantidade = quantidade;
  else itens.push({ produtoId: produto.id, quantidade, preco: produto.preco });
  atualizarTela();
}));

$('pedido').addEventListener('submit', evento => {
  evento.preventDefault();
  const campos = evento.currentTarget.elements;
  executar(() => {
    if (!catalogo || Date.now() - Date.parse(catalogo.baixadoEm) > 48 * 3600000)
      throw new Error('Atualize o catálogo antes de criar o pedido.');
    if (!itens.length) throw new Error('Adicione pelo menos um item.');
    const pedido = {
      id: crypto.randomUUID(), clienteId: campos.cliente.value, catalogoEm: catalogo.baixadoEm,
      parcelas: Number(campos.parcelas.value), itens
    };
    salvar('panificadora.fila', [...fila, pedido]);
    fila.push(pedido);
    itens = [];
    atualizarTela();
    aviso('Pedido salvo neste aparelho.');
  });
});

let sincronizando = false;
async function sincronizar() {
  if (sincronizando) return;
  if (!chave) throw new Error('Informe a chave para transmitir os pedidos.');
  sincronizando = true;
  $('sincronizar').disabled = true;
  try {
    while (fila.length) {
      const pedido = fila[0];
      try { await api('/pedidos', pedido); }
      catch (erro) {
        if (erro.status !== 400) throw erro;
        aviso('Pedido retido: ' + erro.message);
        return;
      }
      const restante = fila.filter(item => item.id !== pedido.id);
      salvar('panificadora.fila', restante);
      fila = restante;
      atualizarTela();
    }
    await atualizar();
    aviso('Fila sincronizada.');
  } finally {
    sincronizando = false;
    $('sincronizar').disabled = false;
  }
}
$('sincronizar').addEventListener('click', () => executar(sincronizar));
window.addEventListener('online', () => { if (chave && fila.length) executar(sincronizar); });

for (const formulario of document.querySelectorAll('[data-rota]')) {
  formulario.addEventListener('submit', evento => {
    evento.preventDefault();
    const campos = Object.fromEntries(new FormData(formulario));
    for (const input of formulario.querySelectorAll('input[type=number]')) campos[input.name] = input.valueAsNumber;
    executar(async () => {
      await api('/gestao/' + formulario.dataset.rota, campos);
      formulario.reset();
      await atualizar();
      aviso('Cadastro registrado.');
    });
  });
}

$('fechar').addEventListener('click', () => executar(async () => {
  const fechados = await api('/gestao/demo/fechar', {});
  await atualizar();
  aviso(fechados + ' pedido(s) processados.');
}));

atualizarTela();
if (chave && navigator.onLine) executar(atualizar);
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/offline.js').catch(() => {});
if (new URLSearchParams(location.search).has('demo')) $('fechar').hidden = false;
