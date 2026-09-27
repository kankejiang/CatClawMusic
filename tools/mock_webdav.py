"""本机假 WebDAV 服务：验证 Kotlin 端的 PROPFIND / 前缀探测 / Basic 认证 / 侧车配对 / 取流播放。

刻意做成「有脾气」的 NAS 形态，逼出真实代码路径：
  * 资源只挂在 /dav 下，根路径 PROPFIND 直接 404 —— 客户端必须靠前缀探测找到 /dav
  * 要求 Basic 认证（cat / p@ss），缺头或错口令一律 401
  * 曲库里有中文文件名（href 用百分号转义）、目录项、同名 .lrc 与同名 .jpg 侧车
  * GET .mp3 回一段真能解码的 WAV，验证 ExoPlayer 走 HTTP 取流

用法：python tools/mock_webdav.py [port]      配合 `adb reverse tcp:5005 tcp:5005`
"""
import base64
import math
import struct
import sys
import io
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import quote, unquote

USER, PASS = "cat", "p@ss"
EXPECTED = "Basic " + base64.b64encode(f"{USER}:{PASS}".encode("utf-8")).decode()

# (name, path, is_dir, size)
ENTRIES = [
    ("dav", "/dav/", True, 0),
    ("Music", "/dav/Music/", True, 0),
    ("中文.mp3", "/dav/Music/%s.mp3" % quote("中文"), False, 4096),
    ("中文.lrc", "/dav/Music/%s.lrc" % quote("中文"), False, 120),
    ("中文.jpg", "/dav/Music/%s.jpg" % quote("中文"), False, 2048),
    ("plain.mp3", "/dav/Music/plain.mp3", False, 3072),
]

LRC = "[00:00.000]mock first line\n[00:02.000]mock second line\n"


def propfind_xml(paths):
    parts = ['<?xml version="1.0" encoding="utf-8"?>',
             '<d:multistatus xmlns:d="DAV:">']
    for name, path, is_dir, size in paths:
        rtype = "<d:collection/>" if is_dir else ""
        length = "" if is_dir else "<d:getcontentlength>%d</d:getcontentlength>" % size
        parts.append(
            "<d:response><d:href>%s</d:href><d:propstat><d:prop>"
            "<d:resourcetype>%s</d:resourcetype>"
            "<d:displayname>%s</d:displayname>%s"
            "<d:getlastmodified>Wed, 15 May 2024 08:00:00 GMT</d:getlastmodified>"
            "</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"
            % (path, rtype, name, length)
        )
    parts.append("</d:multistatus>")
    return "".join(parts).encode("utf-8")


def wav_bytes(seconds=2.0, freq=660.0, rate=44100):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        frames = bytearray()
        for i in range(int(rate * seconds)):
            frames += struct.pack("<h", int(11000 * math.sin(2 * math.pi * freq * i / rate)))
        w.writeframes(bytes(frames))
    return buf.getvalue()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stderr.write("[webdav] %s\n" % (fmt % args))
        sys.stderr.flush()

    def _send(self, payload, ctype="text/xml; charset=utf-8", status=200):
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(payload)

    def _authed(self):
        return self.headers.get("Authorization") == EXPECTED

    def _body(self):
        length = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(length) if length else b""

    def do_PROPFIND(self):
        self._body()  # 读掉请求体，保持 keep-alive 干净
        if not self._authed():
            return self._send(b"need basic", "text/plain", 401)
        raw = self.path.split("?")[0]
        depth = self.headers.get("Depth", "0")
        if raw == "/":
            # 故意不认根：逼客户端去探测 /dav
            return self._send(b"not found", "text/plain", 404)
        if raw.rstrip("/") != "/dav":
            return self._send(b"not found", "text/plain", 404)
        if depth in ("infinity", "899") or int(depth or 0) > 1:
            return self._send(propfind_xml(ENTRIES), status=207)
        # Depth 0/1：只回目录本身与直接子项
        return self._send(propfind_xml(ENTRIES[:2]), status=207)

    def do_GET(self):
        if not self._authed():
            return self._send(b"need basic", "text/plain", 401)
        raw = unquote(self.path.split("?")[0])
        if raw == "/dav/Music/plain.mp3" or raw.endswith("中文.mp3"):
            return self._send(wav_bytes(seconds=20.0), "audio/wav")
        if raw.endswith(".lrc"):
            return self._send(LRC.encode("utf-8"), "text/plain; charset=utf-8")
        return self._send(b"", "application/octet-stream", 404)

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("DAV", "1, 2")
        self.send_header("MS-On-Office", "")
        self.send_header("Allow", "OPTIONS, GET, HEAD, PROPFIND")
        self.send_header("Content-Length", "0")
        self.end_headers()


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5005
    print("mock webdav on 127.0.0.1:%d (user=%s)" % (port, USER), flush=True)
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
