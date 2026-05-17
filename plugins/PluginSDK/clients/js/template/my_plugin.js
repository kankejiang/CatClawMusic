// 猫爪音乐 JavaScript 插件模板 - 歌词提供者
//
// 您的插件业务逻辑写在此文件中。
// 插件接口约定:
//   - getLyrics(title, artist) → { metadata: {...}, lines: [...] } 或 null
//   - getMenuItems(title, artist) → ["10001|菜单1", "10002|菜单2"]
//   - onMenuClicked(itemId, title, artist) → void
//
// 编译流程:
//   1. 编写 my_plugin.js（本文件）
//   2. dotnet build → JsPlugin.dll → JsPlugin.ccp
//   3. 宿主 Assembly.Load() 加载，C# 适配器通过 Jint 引擎执行本文件

var plugin = {
  version: '1.0.0',

  // TODO: 编写您的歌词搜索逻辑
  getLyrics: function(title, artist) {
    if (!title) return null;

    return {
      metadata: {
        title: title,
        artist: artist || '未知'
      },
      lines: [
        { timestamp: '00:01.00', text: 'Hello from JavaScript Plugin!' },
        { timestamp: '00:05.00', text: 'Song: ' + title },
        { timestamp: '00:09.00', text: 'Artist: ' + (artist || 'Unknown') },
        { timestamp: '00:13.00', text: '猫爪音乐 JavaScript 插件示例' },
        { timestamp: '00:17.00', text: 'Powered by Jint on .NET CLR' }
      ]
    };
  },

  getMenuItems: function(title, artist) {
    return ['10001|搜索歌词', '10002|搜索封面'];
  },

  onMenuClicked: function(itemId, title, artist) {
    console.log('[JS Plugin] Menu clicked: ' + itemId);
  }
};
