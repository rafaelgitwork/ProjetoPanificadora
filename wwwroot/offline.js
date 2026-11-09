const versao = 'panificadora-interface-v1';
const arquivos = ['/', '/index.html', '/estilo.css', '/app.js'];
self.addEventListener('install', evento => {
  evento.waitUntil(caches.open(versao).then(cache => cache.addAll(arquivos)).then(() => self.skipWaiting()));
});
self.addEventListener('activate', evento => {
  evento.waitUntil(caches.keys().then(chaves => Promise.all(chaves
    .filter(chave => chave.startsWith('panificadora-interface-') && chave !== versao)
    .map(chave => caches.delete(chave)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', evento => {
  const url = new URL(evento.request.url);
  if (evento.request.method !== 'GET' || url.origin !== location.origin || !arquivos.includes(url.pathname)) return;
  evento.respondWith(fetch(evento.request).catch(() => caches.match(url.pathname)));
});
