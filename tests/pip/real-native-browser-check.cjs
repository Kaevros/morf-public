/* Real installed browser + MV3 extension + registered executable + running Morf.
 * No runtime.connectNative stub, transport replacement, or fake configuration. */
"use strict";
const fs = require("node:fs"), path = require("node:path"), assert = require("node:assert/strict");
const { chromium } = require(process.env.MORF_PLAYWRIGHT_MODULE);
const root = path.resolve(__dirname, "../.."), output = path.join(root, "artifacts/brave-real-native");
fs.mkdirSync(output, { recursive: true });
const profile = fs.mkdtempSync(path.join(output, "profile-"));
const extension = path.join(root, "browser-extension");
let passed = 0;
function check(value, label) { assert.ok(value, label); passed++; console.log("PASS " + label); }
(async () => {
  const context = await chromium.launchPersistentContext(profile, {
    executablePath: process.env.MORF_CHROMIUM_EXECUTABLE, headless: false,
    args: ["--enable-unsafe-extension-debugging", "--disable-extensions-except=" + extension, "--load-extension=" + extension,
      "--autoplay-policy=no-user-gesture-required", "--no-first-run"]
  });
  try {
    context.setDefaultTimeout(10000);
    const worker = context.serviceWorkers()[0] || await context.waitForEvent("serviceworker");
    check(worker.url().includes("kobpckacdnbgcaedocbpbplmkfdmfigd"), "The original MV3 extension loads in the actual Brave executable");
    check(await worker.evaluate(() => selected === null), "An idle extension has no native connection");
    const page = await context.newPage(), errors = [];
    page.on("pageerror", error => errors.push(error.message));
    const liveUrl = process.env.MORF_PIP_LIVE_URL;
    if (!liveUrl) {
    await page.route("**/*", route => route.fulfill({ contentType: "text/html; charset=utf-8", body:
      '<!doctype html><meta charset="utf-8"><title>Morf native Brave check - YouTube</title><div id="movie_player"><div class="html5-video-container"><video muted loop style="width:640px;height:360px"></video></div><div class="ytp-right-controls"></div></div>' }));
    await page.goto("https://www.youtube.com/watch?v=native12345");
    await page.bringToFront();
    await page.evaluate(async () => {
      const canvas = document.createElement("canvas"); canvas.width = 640; canvas.height = 360;
      const drawing = canvas.getContext("2d"), stream = canvas.captureStream(10), chunks = [];
      const recorder = new MediaRecorder(stream, { mimeType: "video/webm;codecs=vp8" });
      recorder.ondataavailable = event => chunks.push(event.data);
      const stopped = new Promise(resolve => recorder.onstop = resolve); recorder.start();
      for (let i = 0; i < 14; i++) {
        drawing.fillStyle = i % 2 ? "#25384b" : "#1c2d3f"; drawing.fillRect(0, 0, 640, 360);
        drawing.fillStyle = "white"; drawing.font = "30px sans-serif"; drawing.fillText("Morf · Real Brave native link", 90, 180);
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      recorder.stop(); await stopped; stream.getTracks().forEach(track => track.stop());
      document.getElementById("movie_player").getVideoData = () => ({ video_id: "native12345", title: "Morf native Brave check", author: "Morf checks" });
      navigator.mediaSession.metadata = new MediaMetadata({ title: "Morf native Brave check", artist: "Morf checks" });
      const video = document.querySelector("video"); video.src = URL.createObjectURL(new Blob(chunks, { type: "video/webm" }));
      await new Promise(resolve => video.onloadeddata = resolve); await video.play();
    });
    } else {
      await page.goto(liveUrl, { waitUntil: "domcontentloaded", timeout: 30000 });
      await page.locator("#movie_player video").waitFor({ state: "attached", timeout: 20000 });
      try { await page.getByText(/^(Tümünü reddet|Reject all)$/i).first().click({ timeout: 6000 }); }
      catch { /* No consent modal in this disposable profile. */ }
      await page.bringToFront();
      await page.locator("#movie_player video").evaluate(video => { video.muted = true; return video.play(); });
      await page.waitForFunction(() => { const video = document.querySelector("#movie_player video"); return !video.paused && video.currentTime > 1; }, null, { timeout: 15000 });
    }
    const deadline = Date.now() + 10000;
    while (!await worker.evaluate(() => selected?.ready === true) && Date.now() < deadline)
      await page.waitForTimeout(100);
    check(await worker.evaluate(() => selected?.ready === true && selected.inline === true),
      "The real registered native host supplies configuration from the running Morf to Brave");
    check(await worker.evaluate(() => selected.advanced === false), "The host's actual saved standard-PiP preference reaches the extension");
    const control = page.locator('button[id^="morf-pip-"][id$="-inline"]');
    await control.waitFor({ state: "attached" });
    await page.locator("#movie_player video").hover(); await control.click();
    await page.waitForFunction(() => document.pictureInPictureElement === document.querySelector("#movie_player video"));
    check(await worker.evaluate(() => selected?.open === true), "A real user-gesture click opens native video PiP and reports its status over actual Native Messaging");
    const before = await page.locator("#movie_player video").evaluate(video => video.currentTime);
    await page.waitForFunction(before => { const video = document.querySelector("#movie_player video"); return !video.paused && video.currentTime !== before; }, before);
    check(true, "Video playback advances while the native PiP window is open");
    check(await page.evaluate(() => !window.documentPictureInPicture?.window && document.querySelectorAll("#movie_player video").length === 1),
      "Standard PiP leaves one source video and has no Document PiP origin header");
    await page.evaluate(() => document.exitPictureInPicture());
    await page.waitForFunction(() => !document.pictureInPictureElement && document.querySelector("#movie_player video").paused);
    const closeDeadline = Date.now() + 5000;
    while (!await worker.evaluate(() => selected === null) && Date.now() < closeDeadline) await page.waitForTimeout(100);
    check(await worker.evaluate(() => selected === null), "Closing PiP pauses the video and releases the real native port");
    check(errors.length === 0, "No uncaught page errors: " + errors.join("; "));
    console.log(JSON.stringify({ passed, browser: await context.browser()?.version(), nativeTransport: "real registered executable and running Morf", source: liveUrl || "generated local HTTPS video fixture" }));
  } finally { await context.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
