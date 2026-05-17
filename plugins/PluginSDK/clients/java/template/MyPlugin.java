/**
 * 猫爪音乐 Java 插件示例 - 歌词提供者
 *
 * 编译流程:
 *   1. javac MyPlugin.java                          → MyPlugin.class
 *   2. jar cf myplugin.jar MyPlugin.class            → myplugin.jar
 *   3. ikvmc -target:library myplugin.jar            → myplugin.dll
 *   4. dotnet build 引用 myplugin.dll + Adapter.cs   → .ccp
 *
 * 接口约定:
 *   - String getLyricsJson(String title, String artist)
 *     返回 JSON: {"metadata":{"title":"...","artist":"..."},"lines":[{...}]}
 *   - byte[] getCover(String title, String artist)
 *     返回封面图片字节
 *   - String[] getMenuItems(String title, String artist)
 *     返回 ["10001|菜单1", "10002|菜单2"]
 */

import java.util.*;

public class MyPlugin {
    public String getVersion() { return "1.0.0"; }

    public String getLyricsJson(String title, String artist) {
        // TODO: 编写您的歌词搜索逻辑
        return "{"
            + "\"metadata\": {\"title\":\"" + escape(title) + "\", \"artist\":\"" + escape(artist) + "\"},"
            + "\"lines\": ["
            + "  {\"timestamp\": \"00:01.00\", \"text\": \"Hello from Java Plugin!\"},"
            + "  {\"timestamp\": \"00:05.00\", \"text\": \"Song: " + escape(title) + "\"},"
            + "  {\"timestamp\": \"00:09.00\", \"text\": \"Artist: " + escape(artist) + "\"},"
            + "  {\"timestamp\": \"00:13.00\", \"text\": \"猫爪音乐 Java 插件示例\"}"
            + "]"
            + "}";
    }

    public String[] getMenuItems(String title, String artist) {
        return new String[] { "10001|搜索歌词", "10002|搜索封面" };
    }

    private String escape(String s) {
        if (s == null) return "";
        return s.replace("\"", "\\\"").replace("\n", "\\n");
    }
}
