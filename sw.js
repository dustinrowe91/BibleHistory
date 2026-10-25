// Offline support. Pages: network first, so an update shows as soon as you're online; the saved copy is used offline
// or if the network takes more than 3 seconds. Fonts: saved copy first, since they never change.
// Bump VERSION when adding or renaming files so old caches are cleared.
const VERSION = "v5";
const CACHE = "bible-history-" + VERSION;
const CORE = [
  "./", "index.html", "books.html", "kings.html", "people.html", "covenants.html", "prophecy.html", "plagues.html", "maps.html", "word-and-deed.html",
  "manifest.webmanifest", "favicon.svg",
  "icons/icon-192.png", "icons/icon-512.png", "icons/icon-maskable-512.png", "icons/apple-touch-icon.png"
];
const FONT_HOSTS = ["fonts.googleapis.com", "fonts.gstatic.com"];

self.addEventListener("install", e => {
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(CORE)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", e => {
  e.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k.startsWith("bible-history-") && k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", e => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (FONT_HOSTS.includes(url.hostname)) { e.respondWith(cacheFirst(req)); return; }
  if (url.origin === location.origin) e.respondWith(networkFirst(req));
});

async function cacheFirst(req) {
  const hit = await caches.match(req);
  if (hit) return hit;
  const res = await fetch(req);
  if (res.ok || res.type === "opaque") (await caches.open(CACHE)).put(req, res.clone());
  return res;
}

async function networkFirst(req) {
  const cache = await caches.open(CACHE);
  const saved = () => cache.match(req, { ignoreSearch: true })
    .then(r => r || (req.mode === "navigate" ? cache.match("index.html") : undefined));
  const net = fetch(req).then(res => {
    if (res.ok) cache.put(req, res.clone());
    return res;
  });
  // Give the network 3 seconds when there is a saved copy to fall back on
  const timeout = new Promise(resolve => setTimeout(resolve, 3000)).then(saved);
  try {
    const res = await Promise.race([net, timeout]);
    if (res) return res;
    return await net;
  } catch (err) {
    const r = await saved();
    if (r) return r;
    throw err;
  }
}
