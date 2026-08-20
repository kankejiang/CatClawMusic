# 猫爪音乐 v1.8.7 更新日志

> 自 v1.8.6 以来共 15 个提交，涵盖插件下载能力、FM 模式抽屉修复与插件加载稳定性。

## ✨ 新增功能

- **插件可调用宿主下载管理器**
  - 新增 `IDownloadManager` 接口（Core）+ DI 别名注册，第三方插件可调宿主下载能力
  - 为网易云等插件下载音源/歌词提供统一入口

## 🛠 修复

- **FM 模式**
  - 模式抽屉高度提升至屏幕 80%，不再被 ViewPager2 宿主钳制
  - 修复滑入动画失效只显示底部一截（逐帧直写原生 translationY）
  - 推荐模式 3 张卡改用 3 等分 Grid 并排，修复第 3 张探索模式卡被挤出屏幕
  - FM 模式标签随切歌同步

- **插件加载**
  - 插件加载前强制运行模块初始化器（RunModuleConstructor），修复部分插件静态状态未初始化
  - 插件入口失败提示用 MainPage 兜底，AppBottomSheet 补充拖拽关闭手势

- **WebView 登录**
  - 优化登录页 Cookie 清理逻辑与依赖更新

## 📦 安装包

- Windows: catclaw.music-1.8.7-Setup.exe（Inno Setup 安装程序）
- Android: com.catclaw.music-1.8.7-Signed.apk（versionCode 64，SHA-1 9fd0613a7c761ea85a48894f4d3566658a892469）
