"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

function harness() {
  let value = null, writes = 0;
  class MediaSession {}
  Object.defineProperty(MediaSession.prototype, "metadata", {
    configurable: true, get() { return value; }, set(next) { value = next; writes++; }
  });
  const events = new Map();
  const data = { video_id: "main1234567", title: "Main video", author: "Main channel" };
  const player = { getVideoData: () => data, querySelector: () => ({ readyState: 4 }) };
  const location = { hostname: "www.youtube.com", pathname: "/watch", href: "https://www.youtube.com/watch?v=main1234567" };
  const context = vm.createContext({ MediaSession, MediaMetadata: class { constructor(value) { Object.assign(this, value); } },
    navigator: { mediaSession: new MediaSession() }, location, URL,
    document: { getElementById: () => player, addEventListener: (name, callback) => events.set(name, callback) }
  });
  vm.runInContext(fs.readFileSync(path.resolve(__dirname, "../../browser-extension/youtube-media.js"), "utf8"), context);
  return { session: context.navigator.mediaSession, data, location, events, get writes() { return writes; } };
}
test("YouTube recommendation metadata cannot replace the watch video's title or artwork", () => {
  const h = harness();
  const main = { title: "Main video", artist: "Main channel", artwork: [{ src: "https://i.ytimg.com/vi/main1234567/hqdefault.jpg" }] };
  h.session.metadata = main;
  h.session.metadata = { title: "Hover preview", artwork: [{ src: "https://preview.invalid/other.jpg" }] };
  assert.equal(h.session.metadata, main);
  assert.equal(h.writes, 1, "a rejected preview must not emit another metadata change to Windows");
});
test("A first preview is repaired from the main video identity without recycling preview artwork", () => {
  const h = harness(); h.session.metadata = { title: "Hover preview", artwork: [{ src: "https://preview.invalid/wrong.jpg" }] };
  assert.equal(h.session.metadata.title, "Main video");
  assert.equal(h.session.metadata.artwork[0].src, "https://i.ytimg.com/vi/main1234567/hqdefault.jpg");
});
test("YouTube navigation and non-watch pages do not retain or force the old video's metadata", () => {
  const h = harness(); h.session.metadata = { title: "Main video" };
  h.location.href = "https://www.youtube.com/watch?v=next1234567";
  const next = { title: "Next video" }; h.session.metadata = next;
  assert.equal(h.session.metadata, next);
  h.data.video_id = "next1234567"; h.data.title = "Next video";
  h.session.metadata = next;
  h.session.metadata = { title: "Another preview" };
  assert.equal(h.session.metadata, next);
  h.location.pathname = "/"; const home = { title: "Home preview" }; h.session.metadata = home;
  assert.equal(h.session.metadata, home);
});
test("Clearing metadata still works and installation itself does no background work", () => {
  const h = harness(); assert.equal(h.writes, 0);
  h.session.metadata = null; assert.equal(h.session.metadata, null);
});
