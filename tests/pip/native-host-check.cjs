"use strict";
const net = require("node:net");
const { spawn } = require("node:child_process");
const assert = require("node:assert/strict");
const { once } = require("node:events");
const executable = process.env.MORF_NATIVE_EXE;
const sid = process.env.MORF_TEST_USER_SID, sessionId = process.env.MORF_TEST_SESSION_ID;
if (!executable || !sid || !sessionId) throw new Error("Set MORF_NATIVE_EXE, MORF_TEST_USER_SID and MORF_TEST_SESSION_ID.");
const pipePath = "\\\\.\\pipe\\Morf.PiP.v1." + sid + "." + sessionId;
const token = "0123456789abcdef0123456789abcdef";
const frame = value => { const body = Buffer.from(JSON.stringify(value)), header = Buffer.alloc(4); header.writeUInt32LE(body.length); return Buffer.concat([header, body]); };
function reader(stream, callback) {
  let buffer = Buffer.alloc(0);
  stream.on("data", data => {
    buffer = Buffer.concat([buffer, data]);
    while (buffer.length >= 4 && buffer.length >= 4 + buffer.readUInt32LE(0)) {
      const count = buffer.readUInt32LE(0); assert.ok(count > 0 && count <= 8192);
      const message = JSON.parse(buffer.subarray(4, 4 + count)); buffer = buffer.subarray(4 + count); callback(message);
    }
  });
}
(async () => {
  let child, socket;
  const server = net.createServer();
  const deadline = setTimeout(() => { child?.kill(); socket?.destroy(); server.close(); console.error("Native host check timed out."); process.exitCode = 1; }, 8000);
  try {
    await new Promise((resolve, reject) => { server.once("error", reject); server.listen(pipePath, resolve); });
    const connected = once(server, "connection");
    child = spawn(executable, ["--pip-native-host", "chrome-extension://kobpckacdnbgcaedocbpbplmkfdmfigd/"], { windowsHide: true, stdio: ["pipe", "pipe", "pipe"] });
    const exited = once(child, "exit");
    child.on("error", error => { console.error(error); process.exitCode = 1; });
    [socket] = await connected;
    const browserMessages = [], morfMessages = [];
    let doneResolve;
    const done = new Promise(resolve => { doneResolve = resolve; });
    reader(socket, message => {
      morfMessages.push(message);
      if (message.type === "hello") {
        assert.equal(message.session, token); assert.equal(message.browser, "chrome");
        socket.write(frame({ version: 1, type: "config", enabled: true, seekSeconds: 10 }));
      } else if (message.type === "status") {
        assert.equal(message.title, "Native host test video");
        socket.write(frame({ version: 1, type: "command", session: token, command: "open" }));
      }
    });
    reader(child.stdout, message => {
      browserMessages.push(message);
      if (message.type === "config") child.stdin.write(frame({ version: 1, type: "status", session: token, browser: "chrome", site: "www.youtube.com", title: "Native host test video", video: true, open: true }));
      else if (message.type === "command") { assert.equal(message.command, "open"); doneResolve(); }
    });
    child.stdin.write(frame({ version: 1, type: "hello", session: token, browser: "chrome" }));
    await done;
    assert.deepEqual(morfMessages.map(message => message.type), ["hello", "status"]);
    assert.deepEqual(browserMessages.map(message => message.type), ["config", "command"]);
    console.log("PASS Actual WinExe native host relays framed browser/Morf messages bidirectionally without WPF startup");
    child.stdin.write(Buffer.from([255, 255, 255, 255]));
    const [code] = await exited;
    assert.equal(code, 3);
    console.log("PASS Actual WinExe native host rejects an oversized browser frame and exits cleanly");
  } finally { clearTimeout(deadline); socket?.destroy(); if (child && child.exitCode === null) child.kill(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; });
