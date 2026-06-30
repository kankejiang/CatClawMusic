"""
为所有二级页面（非主 Tab 页）添加 BackButton 控件到左上角。
- 在 XAML 头部添加 xmlns:controls 命名空间
- 在根 Grid 的最后（即所有内容之上层）插入 <controls:BackButton />
- 主 Tab 页（SearchPage/LibraryPage/NowPlayingPage/SettingsPage）跳过
"""
import re
import sys
from pathlib import Path

PAGES_DIR = Path(r"D:\Code\CatClawMusic\CatClawMusic.Maui\Pages")
MAIN_TAB_PAGES = {
    "SearchPage.xaml",
    "LibraryPage.xaml",
    "NowPlayingPage.xaml",
    "SettingsPage.xaml",
}
CONTROLS_NS = 'xmlns:controls="clr-namespace:CatClawMusic.Maui.Controls"'
BACK_BUTTON_SNIPPET = (
    '        <controls:BackButton Margin="16,16,0,0"\n'
    '                             VerticalOptions="Start"\n'
    '                             HorizontalOptions="Start"\n'
    '                             ZIndex="100" />\n'
)

BACK_BUTTON_MARKER = "<controls:BackButton"

def process_file(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    name = path.name

    # 跳过主 Tab 页
    if name in MAIN_TAB_PAGES:
        return "skip (main tab)"

    # 已添加过则跳过
    if BACK_BUTTON_MARKER in text:
        return "skip (already has back button)"

    # 1. 注入 xmlns:controls 命名空间（紧跟 xmlns:x 之后）
    if CONTROLS_NS not in text:
        # 匹配 xmlns:x="..." 行末
        pattern = re.compile(r'(xmlns:x="[^"]*"\s*)')
        m = pattern.search(text)
        if not m:
            return "fail (no xmlns:x found)"
        insert_pos = m.end()
        # 换行 + 缩进
        text = text[:insert_pos] + "\n             " + CONTROLS_NS + text[insert_pos:]

    # 2. 在最后一个 </Grid> 之前插入 BackButton
    #    假设最后一个 </Grid> 是根 Grid 的闭合
    last_grid_close = text.rfind("</Grid>")
    if last_grid_close < 0:
        return "fail (no </Grid> found)"

    # 找到该行的缩进起点（行首）
    line_start = text.rfind("\n", 0, last_grid_close) + 1
    indent = text[line_start:last_grid_close]
    # 用与 </Grid> 相同的缩进插入
    insertion = BACK_BUTTON_SNIPPET
    text = text[:last_grid_close] + insertion + text[last_grid_close:]

    path.write_text(text, encoding="utf-8")
    return "ok"


def main():
    xaml_files = sorted(PAGES_DIR.glob("*.xaml"))
    results = []
    for f in xaml_files:
        # 只处理 .xaml，不处理 .cs
        if f.suffix != ".xaml":
            continue
        res = process_file(f)
        results.append((f.name, res))
        print(f"  {f.name}: {res}")

    ok = sum(1 for _, r in results if r == "ok")
    skip = sum(1 for _, r in results if r.startswith("skip"))
    fail = sum(1 for _, r in results if r.startswith("fail"))
    print(f"\n汇总: {ok} ok / {skip} skip / {fail} fail / {len(results)} total")
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
