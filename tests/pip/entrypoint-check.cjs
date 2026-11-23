/* Use Chromium's actual launch arguments against an already running Morf.
 * No fake native transport, registrations, saved settings or media commands. */
"use strict";
const { spawn } = require("node:child_process");
const assert = require("node:assert/strict");
const executable = process.env.MORF_NATIVE_EXE;
if (!executable) throw new Error("Set MORF_NATIVE_EXE. A real Morf PiP listener must already be running.");
const origin = "chrome-extension://kobpckacdnbgcaedocbpbplmkfdmfigd/";
const token = "0123456789abcdef0123456789abcdef";
const frame = value => {
  const body = Buffer.from(JSON.stringify(value)), header = Buffer.alloc(4);
  header.writeUInt32LE(body.length); return Buffer.concat([header, body]);
};
async function invoke(args, handshake) {
  const child = spawn(executable, args, { windowsHide: true, stdio: ["pipe", "pipe", "pipe"] });
  let output = Buffer.alloc(0), error = "", config = null;
  const timeout = setTimeout(() => child.kill(), 5000);
  child.stdin.on("error", () => {});
  child.stderr.on("data", data => error += data);
  child.stdout.on("data", data => {
    output = Buffer.concat([output, data]);
    if (handshake && !config && output.length >= 4 && output.length >= 4 + output.readUInt32LE(0)) {
      config = JSON.parse(output.subarray(4, 4 + output.readUInt32LE(0)));
      child.stdin.write(Buffer.from([255, 255, 255, 255]));
    }
  });
  if (handshake) child.stdin.write(frame({ version: 1, type: "hello", session: token, browser: "brave" }));
  else child.stdin.end();
  const code = await new Promise((resolve, reject) => { child.once("error", reject); child.once("exit", resolve); });
  clearTimeout(timeout);
  return { code, config, bytes: output.length, error };
}
(async () => {
  const browser = await invoke([origin, "--parent-window=0"], true);
  assert.ok(browser.config?.type === "config" && browser.config.enabled === true,
    "Real browser-style launch must enter the native host and receive the running Morf's configuration: " + JSON.stringify(browser));
  assert.equal(browser.code, 3, "malformed input must terminate only this native-host child");
  console.log("PASS Exact Chromium/Brave launch arguments receive real Morf configuration, without a synthetic --pip-native-host flag");
  const denied = await invoke(["chrome-extension://untrusted/", "--parent-window=0"], false);
  assert.equal(denied.code, 2); assert.equal(denied.bytes, 0);
  console.log("PASS An untrusted extension invocation exits before WPF or single-instance activation");
  const legacy = await invoke(["--pip-native-host", origin], true);
  assert.equal(legacy.config?.type, "config"); assert.equal(legacy.code, 3);
  console.log("PASS Explicit diagnostic host launches retain the same bounded native protocol");
})().catch(error => { console.error(error); process.exitCode = 1; });
