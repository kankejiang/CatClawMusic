"""把 MAUI 的 51 份 AppResources*.resx 迁移成 Android values-*/app_resources.xml。

用法：python tools/migrate_strings.py
- 中性资源（zh-CN）→ values/app_resources.xml
- 其余 50 份 → values-<locale>/app_resources.xml
- 键名原样保留（PascalCase 合法）；`{0}`/`{1}` 占位符转成 `%1$s`/`%2$s`；
  单引号与 & < > 按 Android 规则转义。
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = Path(__file__).resolve().parents[1]
SRC = REPO / "CatClawMusic.Maui" / "Resources"
DEST = REPO / "CatClawMusic.Android" / "app" / "src" / "main" / "res"

# resx 后缀 → Android 资源限定符目录名
QUALIFIER = {
    "": "",            # 中性：zh-CN
    "zh-TW": "zh-rTW",
    "pt": "pt-rPT",    # resx 里的 pt 是欧洲葡语
    "fil": "fil",
    "nb": "nb",
    "he": "he",
    "is": "is",
    "ur": "ur",
    "sw": "sw",
    "gu": "gu",
    "kn": "kn",
    "ml": "ml",
    "te": "te",
    "bn": "bn",
    "sr": "sr",
}


def qualifier_of(suffix: str) -> str:
    if suffix in QUALIFIER:
        return QUALIFIER[suffix]
    if "-" in suffix:
        lang, _, region = suffix.partition("-")
        return f"{lang}-r{region}"
    return suffix


def escape(value: str) -> str:
    value = re.sub(r"\{(\d+)\}", lambda m: "%" + m.group(1) + "$s", value)
    value = (
        value.replace("\\", "\\\\")
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace("@", "\\@")
        .replace("?", "\\?")
    )
    # Android 会裁掉首尾空白，且裸单引号必须转义
    value = re.sub(r"^\s+", "\u2063", value)
    value = re.sub(r"\s+$", "\u2063", value)
    return value.replace("'", "\\'")


def parse_resx(path: Path) -> dict:
    root = ET.parse(path).getroot()
    out = {}
    for node in root.findall("data"):
        name = node.get("name")
        value = node.find("value")
        if not name or value is None or value.text is None:
            continue
        if name.startswith("Comment_") or name.endswith("_Comment"):
            continue
        out[name] = value.text
    return out


def write_xml(target: Path, entries: dict) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        "<!-- 由 tools/migrate_strings.py 从 MAUI AppResources*.resx 迁移，勿手改 -->",
        "<resources>",
    ]
    for key in sorted(entries):
        lines.append(f'    <string name="{key}">{escape(entries[key])}</string>')
    lines.append("</resources>")
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    files = sorted(SRC.glob("AppResources*.resx"))
    if not files:
        print("找不到 AppResources*.resx")
        return 1

    neutral = parse_resx(SRC / "AppResources.resx")
    write_xml(DEST / "values" / "app_resources.xml", neutral)
    print(f"中性 zh-CN: {len(neutral)} 键")

    locales = 0
    key_counts = []
    for path in files:
        suffix = path.stem.replace("AppResources", "").lstrip(".")
        if not suffix:
            continue
        entries = parse_resx(path)
        write_xml(DEST / f"values-{qualifier_of(suffix)}" / "app_resources.xml", entries)
        locales += 1
        key_counts.append(len(entries))

    print(f"外语: {locales} 个 locale，键数 min={min(key_counts)} max={max(key_counts)}")
    missing = [k for k in neutral if k not in entries]
    print(f"最后一份({suffix})相对中性缺失 {len(missing)} 键")
    return 0


if __name__ == "__main__":
    sys.exit(main())
