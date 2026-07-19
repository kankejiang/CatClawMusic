import os, re, glob

d = r"C:\Code\CatClawMusic\CatClawMusic.Maui\Resources\Images"
base = ["ic_play", "ic_home", "ic_playlist", "ic_library"]
themes = ["9b7ed8", "ec407a", "42a5f5", "66bb6a", "ff7043",
           "ef5350", "26a69a", "ffc107", "5c6bc0", "00bcd4"]
gray = "9aa0b4"

# 清理旧的大写文件，避免 Resizetizer 报无效文件名
for old in glob.glob(os.path.join(d, "*_active.svg")) + glob.glob(os.path.join(d, "*_gray.svg")):
    os.remove(old)

def setfill(content, color):
    return re.sub(r'fill="#?[0-9A-Fa-f]+"', f'fill="#{color}"', content, count=1)

for name in base:
    p = os.path.join(d, name + ".svg")
    content = open(p, encoding="utf-8").read()
    g = setfill(content, gray)
    open(os.path.join(d, f"{name}_gray.svg"), "w", encoding="utf-8").write(g)
    for hx in themes:
        a = setfill(content, hx)
        open(os.path.join(d, f"{name}_{hx}_active.svg"), "w", encoding="utf-8").write(a)

print("generated:", len(glob.glob(os.path.join(d, "*_gray.svg"))), "gray,",
      len(glob.glob(os.path.join(d, "*_active.svg"))), "active")
for f in sorted(glob.glob(os.path.join(d, "ic_play_*_active.svg"))):
    print(os.path.basename(f))
