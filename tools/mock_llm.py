#!/usr/bin/env python3
"""本地假 LLM 端点：只用来给「获取模型列表」这条链路做受控取证。

    python tools/mock_llm.py            # 监听 127.0.0.1:4599
    adb -s <serial> reverse tcp:4599 tcp:4599

设备侧把 API URL 填 `http://127.0.0.1:4599/v1`，Key 随便填，点「获取模型列表」
就应该拿到 MOCK_MODELS 里那几个 id。改这个清单就能验证"列表真的来自服务端"，
而不是页面自己写死的。
"""
import json
import os
from http.server import BaseHTTPRequestHandler, HTTPServer

PORT = int(os.environ.get("MOCK_LLM_PORT", "4599"))
MODELS = [m for m in os.environ.get("MOCK_LLM_MODELS", "mock-alpha,mock-beta,mock-gamma").split(",") if m]


class Handler(BaseHTTPRequestHandler):
    def _send(self, code, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):  # noqa: N802
        # /v1/models 与 /models 都接：拼 URL 的规则一旦写错，这里就会 404，正好当探针
        if self.path.rstrip("/").endswith("/models"):
            self._send(200, {"object": "list", "data": [{"id": m, "object": "model"} for m in MODELS]})
        else:
            self._send(404, {"error": {"message": "no route " + self.path}})

    def do_POST(self):  # noqa: N802
        self._send(200, {"choices": [{"message": {"content": "mock"}, "finish_reason": "stop"}]})

    def log_message(self, fmt, *args):
        print("mock-llm", fmt % args, flush=True)


if __name__ == "__main__":
    print("mock-llm listening on %d, models=%s" % (PORT, MODELS), flush=True)
    HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
