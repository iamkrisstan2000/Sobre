#!/usr/bin/env python3
"""
Serves the Sobre 1.2.2 build for direct download.

Releases live in /home/user/sobre-release — deliberately NOT in a folder named "dist",
because `dist` is excluded from workspace snapshots and the binaries would vanish.
"""
import pathlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

REL = pathlib.Path("/home/user/sobre-release")
PORT = 8000

SETUP_NAME = "Sobre-1.2.2-setup-win-x64.exe"
EXE_NAME = "Sobre-1.2.2-win-x64.exe"
ZIP_NAME = "Sobre-1.2.2-portable-win-x64.zip"
SRC_NAME = "Sobre-1.2.2-source.zip"

FILES = {
    "/": (REL / "download.html", "text/html; charset=utf-8"),
    "/download.html": (REL / "download.html", "text/html; charset=utf-8"),
    f"/{EXE_NAME}": (REL / EXE_NAME, "application/octet-stream"),
    f"/{SETUP_NAME}": (REL / SETUP_NAME, "application/octet-stream"),
    f"/{ZIP_NAME}": (REL / ZIP_NAME, "application/zip"),
    f"/{SRC_NAME}": (REL / SRC_NAME, "application/zip"),
    "/Sobre.exe.sha256": (REL / f"{EXE_NAME}.sha256", "text/plain; charset=utf-8"),
    "/README.md": (REL / "README.md", "text/plain; charset=utf-8"),
    "/LICENSE": (REL / "LICENSE", "text/plain; charset=utf-8"),
    "/icon.png": (REL / "icon.png", "image/png"),
    "/sobre-dark.png": (REL / "preview" / "sobre-dark.png", "image/png"),
    "/sobre-light.png": (REL / "preview" / "sobre-light.png", "image/png"),
}

ALIASES = {
    "/Sobre.exe": f"/{SETUP_NAME}",
    "/setup.exe": f"/{SETUP_NAME}",
    "/lite.zip": f"/{ZIP_NAME}",
    "/source.zip": f"/{SRC_NAME}",
}

class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, fmt, *args):
        if ".exe" in (self.path or "") or ".zip" in (self.path or ""):
            print("download:", self.path, self.headers.get("Range") or "full", flush=True)
    def _resolve(self):
        path = self.path.split("?")[0]
        path = ALIASES.get(path, path)
        entry = FILES.get(path)
        if entry and entry[0].exists():
            return entry
        return None
    def do_HEAD(self):
        entry = self._resolve()
        if not entry:
            self.send_error(404); return
        f, ctype = entry
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(f.stat().st_size))
        self.send_header("Accept-Ranges", "bytes")
        if f.suffix in (".exe", ".zip"):
            self.send_header("Content-Disposition", f'attachment; filename="{f.name}"')
        self.end_headers()
    def do_GET(self):
        entry = self._resolve()
        if not entry:
            self.send_error(404, "Not found"); return
        f, ctype = entry
        size = f.stat().st_size
        start, end, partial = 0, size - 1, False
        rng = self.headers.get("Range")
        if rng and rng.startswith("bytes="):
            try:
                a, _, b = rng[6:].partition("-")
                start = int(a) if a else 0
                end = min(int(b) if b else size - 1, size - 1)
                partial = True
            except ValueError:
                start, end = 0, size - 1
        length = end - start + 1
        self.send_response(206 if partial else 200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(length))
        self.send_header("Accept-Ranges", "bytes")
        if partial:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        if f.suffix in (".exe", ".zip"):
            self.send_header("Content-Disposition", f'attachment; filename="{f.name}"')
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        with open(f, "rb") as fh:
            fh.seek(start)
            remaining = length
            while remaining > 0:
                chunk = fh.read(min(262144, remaining))
                if not chunk: break
                try: self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError): return
                remaining -= len(chunk)

if __name__ == "__main__":
    exe = REL / EXE_NAME
    print(f"serving {exe.name} ({exe.stat().st_size:,} bytes) on 0.0.0.0:{PORT}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
