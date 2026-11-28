"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const crypto = require("node:crypto");
const extension = path.resolve(__dirname, "../../browser-extension");
const policy = require(path.join(extension, "policy.js"));

test("Morf PiP defaults to 10 seconds for YouTube, Netflix and unknown players", () => {
  assert.equal(policy.seekSeconds(), 10);
  assert.equal(policy.seekSeconds(10), 10);
  assert.equal(policy.seekSeconds(5), 5);
  assert.equal(policy.seekSeconds(15), 15);
  for (const value of [0, -5, Infinity, "5", 60, null]) assert.equal(policy.seekSeconds(value), 10);
});
test("Morf cannot inject into Spotify, music sites, browser pages or insecure origins", () => {
  for (const url of ["https://open.spotify.com/track/x", "https://music.youtube.com/", "https://music.apple.com/", "chrome://settings", "http://example.com/video", "https://localhost/", "file:///video.mp4", "https://a:b@example.com/"]) assert.equal(policy.allowedSite(url), false, url);
  for (const url of ["https://www.youtube.com/watch?v=x", "https://www.netflix.com/watch/1", "https://player.example.org/video"]) assert.equal(policy.allowedSite(url), true, url);
});
test("Seeking stays within valid VOD/live ranges and never generates NaN or Infinity", () => {
  assert.equal(policy.seekTarget(20, 10, [[0, 100]]), 30);
  assert.equal(policy.seekTarget(2, -10, [[0, 100]]), 0);
  assert.equal(policy.seekTarget(98, 10, [[0, 100]]), 99.95);
  assert.equal(policy.seekTarget(12, 10, [[0, 15], [30, 50]]), 14.95);
  assert.equal(policy.seekTarget(100, -10, [[95, 110]]), 95);
  assert.equal(policy.seekTarget(20, 10, []), null);
  assert.equal(policy.seekTarget(NaN, 10, [[0, 100]]), null);
  assert.equal(policy.seekTarget(20, Infinity, [[0, 100]]), null);
});
test("Metadata is plain bounded text", () => {
  assert.equal(policy.text(" A\nB\u0000C ", 20), "A B C");
  assert.equal(policy.text("x".repeat(1000), 512).length, 512);
  assert.equal(policy.text("<script>alert(1)</script>", 512), "<script>alert(1)</script>");
});
test("Automatic player controls run only on YouTube and Netflix, without broader host access", () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(extension, "manifest.json")));
  assert.deepEqual(manifest.permissions.sort(), ["activeTab", "nativeMessaging", "scripting"]);
  for (const key of ["host_permissions", "externally_connectable", "web_accessible_resources"]) assert.equal(manifest[key], undefined);
  assert.equal(manifest.content_scripts.length, 2);
  assert.deepEqual(manifest.content_scripts[0].matches, ["https://www.youtube.com/*", "https://www.netflix.com/*"]);
  assert.ok(!manifest.content_scripts[0].all_frames);
  assert.deepEqual(manifest.content_scripts[1].matches, ["https://www.youtube.com/*"]);
  assert.equal(manifest.content_scripts[1].world, "MAIN");
  assert.deepEqual(manifest.content_scripts[1].js, ["youtube-media.js"]);
  const id = [...crypto.createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest("hex").slice(0, 32)].map(c => String.fromCharCode(97 + parseInt(c, 16))).join("");
  const nativeSource = fs.readFileSync(path.join(extension, "../src/Widgates/PictureInPictureHost.cs"), "utf8");
  assert.ok(nativeSource.includes('ExtensionId = "' + id + '"'));
});

function event() {
  const listeners = new Set();
  return { addListener: listener => listeners.add(listener), removeListener: listener => listeners.delete(listener),
    fire: (...args) => { for (const listener of [...listeners]) listener(...args); }, get size() { return listeners.size; } };
}
function harness() {
  const sent = [], calls = [], timers = new Map();
  let timerId = 0;
  const port = { onMessage: event(), onDisconnect: event(), postMessage: message => sent.push(message),
    disconnect: () => { calls.push("disconnect"); port.onDisconnect.fire(); } };
  const api = {
    action: { onClicked: event(), setBadgeText: async data => calls.push(["badge", data]), setTitle: async data => calls.push(["title", data]) },
    runtime: { id: "test-id", onMessage: event(), connectNative: name => { calls.push(["connect", name]); return port; } },
    tabs: { onRemoved: event(), onUpdated: event(), get: async id => ({ id, url: "https://www.youtube.com/watch?v=test", windowId: 1 }),
      sendMessage: async (id, message) => calls.push(["tab-message", id, message]), update: async id => ({ id, windowId: 1 }) },
    windows: { update: async () => {} },
    scripting: { executeScript: async options => { calls.push(["inject", options]); return [{ result: { ok: true } }]; } }
  };
  const context = vm.createContext({ chrome: api, navigator: { userAgent: "Chrome/140" }, URL, crypto,
    setTimeout: fn => { const id = ++timerId; timers.set(id, fn); return id; }, clearTimeout: id => timers.delete(id),
    importScripts: file => vm.runInContext(fs.readFileSync(path.join(extension, file), "utf8"), context) });
  vm.runInContext(fs.readFileSync(path.join(extension, "worker.js"), "utf8"), context);
  return { api, port, sent, calls, timers };
}
const drain = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
test("Idle worker never connects, injects scripts or starts timers", () => {
  const h = harness(); assert.equal(h.calls.length, 0); assert.equal(h.timers.size, 0);
});
test("A playing foreground YouTube tab prepares the player without an extension click or toolbar badge", async () => {
  const h = harness();
  h.api.tabs.sendMessage = async (id, message) => { h.calls.push(["tab-message", id, message]); return { ok: true }; };
  const message = { channel: "morf-pip", type: "inline-ready" };
  const sender = { id: "test-id", tab: { id: 7, active: false }, frameId: 0, url: "https://www.youtube.com/watch?v=test" };
  h.api.runtime.onMessage.fire(message, sender, () => {});
  assert.equal(h.sent.length, 0);
  sender.tab.active = true; let ready;
  h.api.runtime.onMessage.fire(message, sender, response => { ready = response.ok; });
  assert.equal(h.sent[0].type, "hello");
  h.port.onMessage.fire({ version: 1, type: "config", enabled: true, seekSeconds: 10 });
  await drain();
  assert.equal(ready, true);
  assert.ok(h.calls.some(call => call[0] === "tab-message" && call[2].type === "prepare"));
  assert.ok(!h.calls.some(call => ["badge", "inject"].includes(call[0])));
});
test("An explicit action wires native configuration/disconnection and sends only its handshake", () => {
  const h = harness();
  h.api.action.onClicked.fire({ id: 7, url: "https://www.youtube.com/watch?v=test" });
  assert.equal(h.port.onMessage.size, 1);
  assert.equal(h.port.onDisconnect.size, 1);
  assert.equal(h.sent.length, 1); assert.equal(h.sent[0].type, "hello");
  assert.equal(h.calls.filter(c => c[0] === "inject").length, 0);
});
test("Disabled feature cannot inject and releases its one-shot connection/deadline", async () => {
  const h = harness(); h.api.action.onClicked.fire({ id: 7, url: "https://www.youtube.com/watch?v=test" });
  h.port.onMessage.fire({ version: 1, type: "config", enabled: false, seekSeconds: 10 }); await drain();
  assert.equal(h.calls.filter(c => c[0] === "inject").length, 0);
  assert.ok(h.calls.includes("disconnect")); assert.equal(h.timers.size, 0);
});
test("Only the exact selected tab, top frame and session can publish video metadata", async () => {
  const h = harness(); h.api.action.onClicked.fire({ id: 7, url: "https://www.youtube.com/watch?v=test" });
  h.port.onMessage.fire({ version: 1, type: "config", enabled: true, seekSeconds: 10 }); await drain();
  const message = { channel: "morf-pip", type: "status", session: h.sent[0].session, title: "Video", artist: "Channel", open: true };
  const sender = { id: "test-id", tab: { id: 7 }, frameId: 0, url: "https://www.youtube.com/watch?v=test" };
  for (const bad of [{ ...sender, id: "attacker" }, { ...sender, tab: { id: 8 } }, { ...sender, frameId: 1 }, { ...sender, url: "https://attacker.example/" }]) h.api.runtime.onMessage.fire(message, bad);
  assert.equal(h.sent.length, 1);
  h.api.runtime.onMessage.fire({ ...message, session: "f".repeat(32) }, sender); assert.equal(h.sent.length, 1);
  h.api.runtime.onMessage.fire(message, sender); assert.equal(h.sent.length, 2);
  assert.equal(h.sent[1].type, "status"); assert.equal(h.sent[1].site, "www.youtube.com"); assert.equal(h.sent[1].video, true);
});
test("Tab removal releases native host and pending deadline", () => {
  const h = harness(); h.api.action.onClicked.fire({ id: 7, url: "https://www.youtube.com/watch?v=test" });
  h.api.tabs.onRemoved.fire(7); assert.ok(h.calls.includes("disconnect")); assert.equal(h.timers.size, 0);
});
test("Loading notifications and same-origin SPA changes retain PiP; cross-origin navigation releases it", () => {
  const h = harness(); h.api.action.onClicked.fire({ id: 7, url: "https://www.youtube.com/watch?v=test" });
  h.api.tabs.onUpdated.fire(7, { url: "https://www.youtube.com/watch?v=next" });
  assert.ok(!h.calls.includes("disconnect"));
  h.api.tabs.onUpdated.fire(7, { status: "loading" });
  assert.ok(!h.calls.includes("disconnect"));
  h.api.tabs.onUpdated.fire(7, { url: "https://example.org/video" });
  assert.ok(h.calls.includes("disconnect"));
});
test("Completed navigation checks the selected document without polling and releases a lost document", async () => {
  const h = harness(); h.api.action.onClicked.fire({ id: 7, url: "https://www.youtube.com/watch?v=test" });
  h.port.onMessage.fire({ version: 1, type: "config", enabled: true, seekSeconds: 10 }); await drain();
  h.api.tabs.sendMessage = async (id, message) => ({ session: message.session });
  h.api.tabs.onUpdated.fire(7, { status: "complete" }); await drain();
  assert.ok(!h.calls.includes("disconnect")); assert.equal(h.timers.size, 0);
  h.api.tabs.sendMessage = async () => undefined;
  h.api.tabs.onUpdated.fire(7, { status: "complete" }); await drain();
  assert.ok(h.calls.includes("disconnect")); assert.equal(h.timers.size, 0);
});
