"use strict";
importScripts("policy.js");
const api = globalThis.browser || chrome;
const policy = MorfPipPolicy;
let selected = null;

function browserName() {
  const ua = navigator.userAgent;
  if (ua.includes("Edg/")) return "edge";
  if (ua.includes("OPR/")) return "opera";
  if (ua.includes("Vivaldi/")) return "vivaldi";
  if (ua.includes("Firefox/")) return "firefox";
  return "chrome"; // Chromium browsers with a Chrome UA remain source-checked by Morf.
}
function badge(tabId, message = "") {
  void api.action.setTitle({ tabId, title: message || "Videoyu Morf PiP ile aç" }).catch(() => {});
}
function finish(state, notify = true) {
  if (!state || state.closed) return;
  state.closed = true;
  state.complete?.(false);
  clearTimeout(state.deadline);
  if (selected === state) selected = null;
  if (notify) void api.tabs.sendMessage(state.tabId, { channel: "morf-pip", session: state.session, type: "dispose" }).catch(() => {});
  try { state.port.disconnect(); } catch { /* Already disconnected. */ }
}
function nativeMessage(state, message) {
  if (selected !== state || state.closed || !message || message.version !== 1) return;
  if (message.type === "config") {
    if (!message.enabled) {
      badge(state.tabId, "Morf ayarlarından Pencere içinde video · PiP seçeneğini açın.");
      finish(state); return;
    }
    state.seconds = policy.seekSeconds(message.seekSeconds);
    state.advanced = message.advanced === true;
    if (state.ready) {
      void api.tabs.sendMessage(state.tabId, { channel: "morf-pip", session: state.session, type: "config", seekSeconds: state.seconds, advanced: state.advanced }).catch(() => finish(state));
    } else if (!state.starting) {
      state.starting = true;
      void openVideo(state);
    }
  } else if (message.type === "command" && message.session === state.session && message.command === "open" && state.ready) {
    // Focus an open player, or use the content script's browser-activation fallback.
    void api.tabs.sendMessage(state.tabId, { channel: "morf-pip", session: state.session, type: "open" }).catch(() => finish(state));
  }
}
async function openVideo(state) {
  try {
    const tab = await api.tabs.get(state.tabId);
    if (!state.inline && (!policy.allowedSite(tab.url) || new URL(tab.url).origin !== state.origin)) throw new Error("source-changed");
    const config = { session: state.session, seekSeconds: state.seconds, advanced: state.advanced };
    let result;
    if (state.inline) {
      result = await api.tabs.sendMessage(state.tabId, { channel: "morf-pip", type: "prepare", ...config },
        state.documentId ? { documentId: state.documentId } : { frameId: 0 });
    } else {
      await api.scripting.executeScript({ target: { tabId: state.tabId }, files: ["policy.js", "content.js"] });
      if (selected !== state || state.closed) return;
      const results = await api.scripting.executeScript({
        target: { tabId: state.tabId },
        func: config => globalThis.__morfPip.open(config),
        args: [config]
      });
      result = results[0]?.result;
    }
    if (selected !== state || state.closed) return;
    if (!result?.ok) {
      badge(state.tabId, result?.reason === "unsupported" ? "Bu tarayıcıda gelişmiş PiP desteklenmiyor. Tarayıcının kendi PiP özelliğini kullanabilirsiniz."
        : result?.reason === "video" ? "Bu sekmede uygun video bulunamadı. Videonun kendi sayfasında yeniden deneyin." : "Video PiP'ye alınamadı. Kaynak oynatıcıyı kontrol edin.");
      finish(state); return;
    }
    state.ready = true;
    state.complete(true);
    clearTimeout(state.deadline);
    badge(state.tabId, result.activation ? "PiP'yi açmak için videonun üzerindeki Morf düğmesine tıklayın." : "");
    await api.tabs.sendMessage(state.tabId, { channel: "morf-pip", session: state.session, type: "publish" });
  } catch {
    if (!state.closed) badge(state.tabId, "Video bağlantısı kurulamadı. Videonun kendi sekmesinden yeniden deneyin.");
    finish(state);
  }
}
function connect(tab, inline) {
  finish(selected);
  const session = crypto.randomUUID().replaceAll("-", "");
  let port;
  try { port = api.runtime.connectNative("com.morf.pip"); }
  catch { badge(tab.id, "Morf bağlantısı bulunamadı. Morf ayarlarından PiP'yi açın."); return null; }
  const state = { tabId: tab.id, documentId: tab.documentId, origin: new URL(tab.url).origin, site: new URL(tab.url).hostname,
    session, port, inline, ready: false, closed: false, seconds: 10 };
  state.prepared = new Promise(resolve => { state.complete = resolve; });
  selected = state;
  state.deadline = setTimeout(() => {
    badge(state.tabId, "Morf yanıt vermedi. Uygulamanın açık ve PiP özelliğinin etkin olduğunu kontrol edin.");
    finish(state);
  }, 6000);
  port.onMessage.addListener(message => nativeMessage(state, message));
  port.onDisconnect.addListener(() => {
    void api.runtime.lastError;
    if (!state.closed) badge(state.tabId, "Morf bağlantısı kapandı. PiP'yi yeniden başlatabilirsiniz.");
    finish(state);
  });
  port.postMessage({ version: 1, type: "hello", session, browser: browserName() });
  return state;
}
api.action.onClicked.addListener(tab => {
  if (!tab.id || !policy.allowedSite(tab.url)) {
    if (tab.id) badge(tab.id, "Morf PiP, HTTPS video sayfalarında çalışır.");
    return;
  }
  const state = selected;
  if (state?.tabId === tab.id && state.ready) {
    void api.tabs.sendMessage(tab.id, { channel: "morf-pip", session: state.session, type: "open" }).catch(() => finish(state));
    return;
  }
  connect(tab, false);
});
api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id || !sender.tab?.id || sender.frameId !== 0 || message?.channel !== "morf-pip") return;
  if (message.type === "inline-ready" || message.type === "inline-click") {
    if (!policy.allowedSite(sender.url) || !sender.tab.active) return;
    const url = new URL(sender.url);
    if (!["www.youtube.com", "www.netflix.com"].includes(url.hostname)) return;
    const same = selected?.tabId === sender.tab.id && selected.origin === url.origin
      && (!selected.documentId || selected.documentId === sender.documentId);
    if (message.type === "inline-ready" && selected?.open && !same) return;
    const state = same ? selected : connect({ id: sender.tab.id, url: sender.url, documentId: sender.documentId }, true);
    if (!state) return;
    void state.prepared.then(ok => respond({ ok }));
    return true;
  }
  const state = selected;
  if (!state || state.closed || sender.tab.id !== state.tabId || message.session !== state.session) return;
  if (state.documentId && sender.documentId !== state.documentId) return;
  try { if (new URL(sender.url).origin !== state.origin) return; } catch { return; }
  if (message.type === "closed") { finish(state, false); badge(state.tabId); return; }
  if (message.type === "activation") {
    // Focus only after the user's explicit Morf artwork click. Never move other windows.
    void api.tabs.update(state.tabId, { active: true }).then(tab => api.windows.update(tab.windowId, { focused: true })).catch(() => {});
    badge(state.tabId, "Videonun üzerindeki PiP'yi aç düğmesine tıklayın.");
    return;
  }
  if (message.type !== "status" || typeof message.title !== "string") return;
  const title = policy.text(message.title, 512);
  if (!title) return;
  try {
    state.open = message.open === true;
    state.port.postMessage({ version: 1, type: "status", session: state.session, browser: browserName(), site: state.site,
      title, artist: policy.text(message.artist, 256), video: true, open: state.open, inline: state.inline && message.inline === true });
  } catch { finish(state); }
});
api.tabs.onRemoved.addListener(tabId => { if (selected?.tabId === tabId) finish(selected, false); });
api.tabs.onUpdated.addListener((tabId, change) => {
  if (selected?.tabId !== tabId) return;
  // A loading notification is not proof that the selected document was destroyed.
  // Same-document video transitions keep their port; actual pagehide sends "closed".
  if (change.status === "complete" && selected.ready) {
    const state = selected;
    // A document-scoped acknowledgement also releases a host if navigation dropped
    // the unloading document's final message. No heartbeat or retry timer.
    void api.tabs.sendMessage(tabId, { channel: "morf-pip", session: state.session, type: "probe" },
      state.documentId ? { documentId: state.documentId } : { frameId: 0 })
      .then(reply => { if (reply?.session !== state.session) finish(state); }).catch(() => finish(state));
  }
  if (change.url) {
    try { if (new URL(change.url).origin !== selected.origin) finish(selected); }
    catch { finish(selected); }
  }
});
