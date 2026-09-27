"""本机假 Subsonic/Navidrome 服务：只覆盖 App 真会打的端点，用来在真机上端到端验证远程音源。

用法：python tools/mock_subsonic.py            （监听 127.0.0.1:4535，配合 `adb reverse tcp:4535 tcp:4535`）
刻意留的两个坑位：
  * al-2 只有一张专辑里的曲目，返回的是**对象**而不是数组（Subsonic 的真实退化行为）
  * stream.view 回一段真能播的 WAV，验证 ExoPlayer 走的是 HTTP 取流而不是本地文件
"""
import json
import math
import struct
import sys
import wave
import io
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

TOKEN = None  # 不校验 token，只记录收到的认证参数


def wav_bytes(seconds=2.0, freq=440.0, rate=44100):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        frames = bytearray()
        for i in range(int(rate * seconds)):
            value = int(12000 * math.sin(2 * math.pi * freq * i / rate))
            frames += struct.pack("<h", value)
        w.writeframes(bytes(frames))
    return buf.getvalue()


ALBUMS = [
    {"id": "al-1", "name": "Rumours", "artist": "Fleetwood Mac", "artistId": "ar-1"},
    {"id": "al-2", "name": "Tusk", "artist": "Fleetwood Mac", "artistId": "ar-1"},
]
TRACKS = {
    "al-1": [
        {"id": "tr-1", "title": "The Chain", "album": "Rumours", "albumId": "al-1",
         "artist": "Fleetwood Mac", "artistId": "ar-1", "duration": 264, "size": 1048576,
         "bitRate": 320, "coverArt": "cv-1", "type": "audio", "isDir": False, "year": 1977, "track": 7},
        {"id": "tr-2", "title": "Dreams", "album": "Rumours", "albumId": "al-1",
         "artist": "Fleetwood Mac", "artistId": "ar-1", "duration": 257, "size": 1048576,
         "bitRate": 320, "type": "audio", "isDir": False, "year": 1977, "track": 6},
    ],
    # 故意只有一首，且用对象而不是数组
    "al-2": {"id": "tr-3", "title": "Over & Over", "album": "Tusk", "albumId": "al-2",
             "artist": "Fleetwood Mac", "artistId": "ar-1", "duration": 210, "size": 1048576,
             "bitRate": 192, "type": "audio", "isDir": False, "year": 1979, "track": 1},
}


def envelope(body):
    return json.dumps({"subsonic-response": {"status": "ok", "version": "1.16.1", **body}}).encode()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stderr.write("[mock] %s\n" % (fmt % args))
        sys.stderr.flush()

    def _send(self, payload, ctype="application/json", status=200):
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        path = parsed.path
        if path == "/rest/ping.view":
            self._send(envelope({"openSubsonic": {"version": "0.55.7"}}))
        elif path == "/rest/getAlbumList2.view":
            offset = int(query.get("offset", ["0"])[0])
            size = int(query.get("size", ["200"])[0])
            page = ALBUMS[offset:offset + size]
            self._send(envelope({"albumList2": {"album": page}}))
        elif path == "/rest/getAlbum.view":
            album_id = query.get("id", [""])[0]
            songs = TRACKS.get(album_id, [])
            self._send(envelope({"album": {"id": album_id, "name": album_id, "song": songs}}))
        elif path == "/rest/search3.view":
            all_tracks = [t for v in TRACKS.values() for t in (v if isinstance(v, list) else [v])]
            self._send(envelope({"searchResult3": {"song": all_tracks}}))
        elif path == "/rest/stream.view":
            self._send(wav_bytes(), ctype="audio/wav")
        elif path == "/rest/getLyricsBySongId.view":
            self._send(envelope({"lyricsList": {"structuredLyrics": {"lang": "z", "line": [
                {"start": 0, "value": "mock line one"}, {"start": 1500, "value": "mock line two"}]}}}))
        else:
            self._send(envelope({}))


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 4535
    print("mock subsonic on 127.0.0.1:%d" % port, flush=True)
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
