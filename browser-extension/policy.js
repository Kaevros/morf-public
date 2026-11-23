/* Shared by the extension and its offline checks. No browser API or background work. */
(() => {
  "use strict";
  const text = (value, limit) => String(value ?? "").replace(/[\u0000-\u001f\u007f-\u009f]/g, " ").trim().slice(0, limit);
  const musicSite = host => host === "spotify.com" || host.endsWith(".spotify.com") || host === "music.apple.com" || host === "music.youtube.com";
  const allowedSite = value => {
    try {
      const url = new URL(value);
      return url.protocol === "https:" && !url.username && !url.password && !url.port && !musicSite(url.hostname)
        && !["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
    } catch { return false; }
  };
  const seekSeconds = value => value === 5 || value === 15 ? value : 10;
  const seekTarget = (position, offset, ranges) => {
    if (!Number.isFinite(position) || !Number.isFinite(offset) || !ranges.length) return null;
    const wanted = position + offset;
    let closest = null;
    for (const [start, end] of ranges) {
      if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) continue;
      const candidate = Math.max(start, Math.min(Math.max(start, end - 0.05), wanted));
      if (closest === null || Math.abs(candidate - wanted) < Math.abs(closest - wanted)) closest = candidate;
    }
    return closest;
  };
  globalThis.MorfPipPolicy = Object.freeze({ text, musicSite, allowedSite, seekSeconds, seekTarget });
  if (typeof module !== "undefined") module.exports = globalThis.MorfPipPolicy;
})();
