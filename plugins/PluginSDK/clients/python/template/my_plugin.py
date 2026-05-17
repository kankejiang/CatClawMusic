"""
猫爪音乐 Python 插件模板 - 歌词提供者

您的插件业务逻辑写在此文件中。
插件接口约定:
  - get_lyrics(title, artist) → {:metadata => {:title => "", :artist => ""}, :lines => [{timestamp: "00:01.00", text: "..."}]}
    若未找到歌词，返回 None
  - get_menu_items(title, artist) → [{"MENU_ID|菜单标题"}]
  - on_menu_clicked(item_id, title, artist) → None

编译流程:
  1. 编写 my_plugin.py（本文件）
  2. dotnet build → PythonPlugin.dll → PythonPlugin.ccp
  3. 宿主 Assembly.Load() 加载，C# 适配器通过 IronPython 引擎执行本文件
"""

class MyPlugin:
    def get_version(self) -> str:
        return "1.0.0"

    def get_plugin_id(self) -> str:
        return "python.plugin"

    def get_plugin_name(self) -> str:
        return "Python 插件"

    def get_lyrics(self, title: str, artist: str) -> dict:
        """TODO: 编写您的歌词搜索逻辑"""
        if not title:
            return None

        return {
            "metadata": {
                "title": title,
                "artist": artist or "未知"
            },
            "lines": [
                {"timestamp": "00:01.00", "text": "Hello from Python Plugin!"},
                {"timestamp": "00:05.00", "text": f"Song: {title}"},
                {"timestamp": "00:09.00", "text": f"Artist: {artist}"},
                {"timestamp": "00:13.00", "text": "猫爪音乐 Python 插件示例"}
            ]
        }

    def get_menu_items(self, title: str, artist: str) -> list:
        return ["10001|搜索歌词", "10002|搜索封面"]

    def on_menu_clicked(self, item_id: int, title: str, artist: str) -> None:
        print(f"[Python Plugin] Menu clicked: {item_id}")

# 入口 - 宿主通过此类名查找
plugin = MyPlugin()
