<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>🐾 猫爪音乐 CatClaw Music · 萌系音乐播放器</title>
<style>
  :root{
    --cream:#FFF9FB; --cream2:#F3EEFF; --card:#FFFFFF;
    --lav:#B9A7FF; --lav-d:#8C7BFF; --sky:#8FD3FF; --sky-d:#55D6FF;
    --pink:#FFB6D5; --mint:#9FF0D0; --peach:#FFD6A5;
    --ink:#4A4063; --ink2:#6E6491; --ink3:#9A90BC;
    --line:#ECE6FB; --shadow:0 10px 30px -12px rgba(140,123,255,.35);
    --shadow-s:0 6px 16px -10px rgba(140,123,255,.45);
    --radius:26px;
    --display:'PingFang SC','HarmonyOS Sans SC','Quicksand',system-ui,sans-serif;
    --body:'PingFang SC','HarmonyOS Sans SC',system-ui,sans-serif;
  }
  *{box-sizing:border-box;margin:0;padding:0;scroll-behavior:smooth}
  html,body{background:var(--cream);font-family:var(--body);color:var(--ink);line-height:1.7;-webkit-font-smoothing:antialiased}
  a{color:var(--lav-d);text-decoration:none}
  ::selection{background:var(--pink);color:#fff}

  /* 背景装饰 */
  body::before{content:"";position:fixed;inset:0;z-index:-2;
    background:
      radial-gradient(700px 500px at 12% -5%,rgba(185,167,255,.35),transparent 60%),
      radial-gradient(620px 460px at 100% 0%,rgba(143,211,255,.32),transparent 58%),
      radial-gradient(560px 520px at 50% 110%,rgba(255,182,213,.26),transparent 60%),
      var(--cream);
  }
  .paw{position:fixed;z-index:-1;font-size:26px;opacity:.18;pointer-events:none;animation:float 9s ease-in-out infinite}
  @keyframes float{0%,100%{transform:translateY(0) rotate(-8deg)}50%{transform:translateY(-22px) rotate(8deg)}}

  .wrap{max-width:980px;margin:0 auto;padding:0 18px}

  /* 顶栏 */
  .nav{position:sticky;top:0;z-index:30;backdrop-filter:blur(14px);
    background:rgba(255,249,251,.82);border-bottom:1px solid var(--line);
    display:flex;align-items:center;gap:14px;padding:11px 18px;flex-wrap:wrap}
  .brand{font-family:var(--display);font-weight:800;font-size:17px;display:flex;align-items:center;gap:7px}
  .brand .emo{font-size:20px}
  .nav-links{display:flex;gap:6px;margin-left:auto;flex-wrap:wrap}
  .nav-links a{font-size:13px;color:var(--ink2);padding:6px 12px;border-radius:999px;transition:.18s}
  .nav-links a:hover{background:var(--cream2);color:var(--lav-d)}

  /* Hero */
  .hero{position:relative;text-align:center;padding:64px 18px 40px;overflow:hidden}
  .hero-badge{display:inline-flex;align-items:center;gap:7px;background:#fff;border:1px solid var(--line);
    color:var(--lav-d);font-size:12.5px;font-weight:700;padding:6px 14px;border-radius:999px;box-shadow:var(--shadow-s);margin-bottom:22px}
  .hero h1{font-family:var(--display);font-size:clamp(34px,7vw,56px);font-weight:800;letter-spacing:-1px;line-height:1.1}
  .hero h1 .grad{background:linear-gradient(120deg,var(--lav-d),var(--sky-d) 70%);-webkit-background-clip:text;background-clip:text;color:transparent}
  .hero p{max-width:620px;margin:18px auto 0;color:var(--ink2);font-size:15px}
  .badges{display:flex;gap:8px;flex-wrap:wrap;justify-content:center;margin-top:26px}
  .badges img{height:26px;border-radius:8px;box-shadow:var(--shadow-s)}

  /* 通用卡片 */
  .card{background:var(--card);border:1px solid var(--line);border-radius:var(--radius);box-shadow:var(--shadow);padding:26px}
  section{padding:30px 0}
  .sec-title{font-family:var(--display);font-size:24px;font-weight:800;display:flex;align-items:center;gap:10px;margin-bottom:4px}
  .sec-sub{color:var(--ink3);font-size:13.5px;margin-bottom:20px}
  .reveal{opacity:0;transform:translateY(18px);transition:.6s cubic-bezier(.16,1,.3,1)}
  .reveal.in{opacity:1;transform:none}

  /* QQ 群 CTA */
  .qq{display:flex;align-items:center;gap:20px;flex-wrap:wrap;justify-content:center;text-align:center;
    background:linear-gradient(120deg,var(--lav),var(--sky));border:0;color:#fff;box-shadow:0 16px 36px -14px rgba(140,123,255,.7)}
  .qq .big{font-family:var(--display);font-size:22px;font-weight:800}
  .qq .sm{font-size:13px;opacity:.92;margin-top:4px}
  .qq a{display:inline-flex;align-items:center;gap:8px;background:#fff;color:var(--lav-d);font-weight:700;
    padding:11px 20px;border-radius:14px;font-size:14px;box-shadow:var(--shadow-s);transition:transform .14s}
  .qq a:active{transform:scale(.96)}

  /* 截图（默认收起） */
  .shots-head{display:flex;align-items:center;gap:10px;cursor:pointer;user-select:none}
  .shots-head .chev{margin-left:auto;width:30px;height:30px;border-radius:50%;display:grid;place-items:center;
    background:#fff;border:1px solid var(--line);color:var(--lav-d);font-size:15px;transition:transform .25s ease;box-shadow:var(--shadow-s)}
  .shots-head.open .chev{transform:rotate(180deg)}
  .shots-sub{color:var(--ink3);font-size:13.5px;margin:0 0 0 34px;transition:.25s}
  .shots-wrap{display:grid;grid-template-rows:0fr;transition:grid-template-rows .35s ease;margin-top:0}
  .shots-wrap.open{grid-template-rows:1fr;margin-top:20px}
  .shots-inner{overflow:hidden}
  .shots{display:grid;grid-template-columns:repeat(2,1fr);gap:16px}
  .shot{background:#fff;border:1px solid var(--line);border-radius:20px;overflow:hidden;box-shadow:var(--shadow-s);transition:transform .2s,box-shadow .2s}
  .shot:hover{transform:translateY(-4px);box-shadow:var(--shadow)}
  .shot img{width:100%;display:block;aspect-ratio:9/19;object-fit:cover;background:var(--cream2)}
  .shot .cap{font-size:13px;font-weight:600;color:var(--ink2);text-align:center;padding:10px;background:#fff}
  .shots-toggle{cursor:pointer}

  /* 架构树 */
  .tree{background:#2A2350;color:#E7E2FF;border-radius:20px;padding:22px;font-family:'SFMono-Regular',Consolas,monospace;font-size:12.5px;line-height:1.75;overflow:auto}
  .tree .c1{color:#B9A7FF}.tree .c2{color:#8FD3FF}.tree .c3{color:#9FF0D0}.tree .c4{color:#FFD6A5}.tree .dim{color:#8A82B8}
  .stack{display:flex;flex-wrap:wrap;gap:8px;margin-top:14px}
  .stack span{background:var(--cream2);border:1px solid var(--line);color:var(--ink2);font-size:12px;padding:5px 12px;border-radius:999px;font-weight:600}

  /* 功能卡 */
  .feat-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:16px}
  .feat{background:#fff;border:1px solid var(--line);border-radius:20px;padding:20px;box-shadow:var(--shadow-s);transition:transform .2s}
  .feat:hover{transform:translateY(-3px)}
  .feat h3{font-family:var(--display);font-size:16.5px;font-weight:800;display:flex;align-items:center;gap:9px;margin-bottom:10px}
  .feat h3 .ic{width:34px;height:34px;border-radius:11px;display:grid;place-items:center;font-size:18px;flex:0 0 auto;
    background:linear-gradient(135deg,var(--lav),var(--sky));box-shadow:var(--shadow-s)}
  .feat ul{list-style:none;display:flex;flex-direction:column;gap:7px}
  .feat li{font-size:13px;color:var(--ink2);padding-left:18px;position:relative}
  .feat li::before{content:"🐾";position:absolute;left:0;top:1px;font-size:10px;opacity:.7}
  .feat li b{color:var(--ink)}

  /* 表格 */
  .tbl{width:100%;border-collapse:separate;border-spacing:0;font-size:13px;overflow:hidden;border-radius:18px;border:1px solid var(--line);background:#fff}
  .tbl th{background:linear-gradient(120deg,var(--lav),var(--sky));color:#fff;text-align:left;padding:12px 14px;font-weight:700;font-size:13px}
  .tbl td{padding:11px 14px;border-top:1px solid var(--line);color:var(--ink2);vertical-align:top}
  .tbl tr:nth-child(even) td{background:var(--cream)}
  .tbl td:first-child{font-weight:600;color:var(--ink);white-space:nowrap}

  /* 更新日志 */
  .log{border-left:3px solid var(--lav);padding-left:18px;margin-left:4px}
  .ver{font-family:var(--display);font-weight:800;font-size:18px;color:var(--lav-d);margin-top:18px;display:flex;align-items:center;gap:8px}
  .ver:first-child{margin-top:0}
  .tag{display:inline-block;font-size:11px;font-weight:700;padding:2px 9px;border-radius:999px;margin-right:6px}
  .tag.fix{background:#FFE0EC;color:#E0457F}.tag.new{background:#E0F7EC;color:#1E9E63}.tag.tech{background:#E4ECFF;color:#3A6FE0}
  .log li{font-size:13px;color:var(--ink2);margin:7px 0 7px 18px}

  .pill-row{display:flex;flex-wrap:wrap;gap:8px}
  .pill{background:#fff;border:1px solid var(--line);border-radius:999px;padding:7px 14px;font-size:12.5px;color:var(--ink2);box-shadow:var(--shadow-s);font-weight:600}

  footer{text-align:center;padding:40px 18px 60px;color:var(--ink3);font-size:13px}
  footer .heart{color:var(--pink)}

  @media(max-width:680px){
    .shots,.feat-grid{grid-template-columns:1fr}
    .nav-links{display:none}
  }
</style>
</head>
<body>

<!-- 漂浮猫爪 -->
<div class="paw" style="left:6%;top:18%">🐾</div>
<div class="paw" style="left:90%;top:12%;animation-delay:1.5s">🐾</div>
<div class="paw" style="left:14%;top:62%;animation-delay:3s">🐾</div>
<div class="paw" style="left:82%;top:70%;animation-delay:2.2s">🐾</div>
<div class="paw" style="left:48%;top:84%;animation-delay:4s">🐾</div>

<!-- 顶栏 -->
<nav class="nav">
  <div class="brand"><span class="emo">🐾</span> 猫爪音乐</div>
  <div class="nav-links">
    <a href="#screens">截图</a>
    <a href="#arch">架构</a>
    <a href="#feat">功能</a>
    <a href="#changelog">更新</a>
    <a href="#db">数据库</a>
  </div>
</nav>

<!-- Hero -->
<header class="hero">
  <div class="hero-badge">🐱 萌系 Android 音乐播放器 · .NET 10 + C# 13 原生</div>
  <h1>🐾 <span class="grad">猫爪音乐</span><br/>CatClaw Music</h1>
  <p>一只会陪你听歌的猫爪 ♪ 支持本地音乐、Navidrome/Subsonic 网络音乐、WebDAV、SMB/CIFS，还有桌面悬浮歌词、逐字高亮、音频频谱、AI 对话式搜索、均衡器与全套萌系体验。</p>
  <div class="badges">
    <img alt="平台" src="https://img.shields.io/badge/平台-Android-green" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4" />
    <img alt="C#" src="https://img.shields.io/badge/C%23-13.0-blue" />
    <img alt="版本" src="https://img.shields.io/badge/版本-1.6.4-ff69b4" />
    <img alt="最低版本" src="https://img.shields.io/badge/最低版本-Android%2012%20(API%2031)-orange" />
    <img alt="协议" src="https://img.shields.io/badge/协议-MIT-yellow" />
  </div>
</header>

<div class="wrap">

  <!-- QQ 群 -->
  <section class="reveal">
    <div class="card qq">
      <div>
        <div class="big">🐾 加入猫爪音乐交流群</div>
        <div class="sm">一起撸猫、一起听歌、一起提需求 ₍˄·͈༝·͈˄*₎◞ ̑̑</div>
      </div>
      <a href="https://qm.qq.com/q/Fhu3IEzqa4" target="_blank">💬 点击加入 QQ 群 855383639</a>
    </div>
  </section>

  <!-- 截图（默认收起） -->
  <section id="screens" class="reveal">
    <div class="shots-head" id="shotsHead">
      <span class="sec-title" style="margin:0">📱 应用截图</span>
      <span class="chev">▾</span>
    </div>
    <div class="shots-sub">点击标题展开 7 张截图，点小图可放大看细节喵～</div>
    <div class="shots-wrap" id="shotsWrap">
      <div class="shots-inner">
        <div class="shots">
          <div class="shot"><img loading="lazy" src="images/213D14E63AAD1FD2FB2431EBDE73589C.jpg" alt="播放页面"/><div class="cap">▶️ 播放页面</div></div>
          <div class="shot"><img loading="lazy" src="images/e4fc2f068444f8a1e1b82338bf5fa380.jpg" alt="歌词页面"/><div class="cap">🎶 歌词页面</div></div>
          <div class="shot"><img loading="lazy" src="images/10847a3c0f88f601a4522567ce77888b.jpg" alt="歌曲详情"/><div class="cap">💿 歌曲详情</div></div>
          <div class="shot"><img loading="lazy" src="images/714ab1a33c755ee2066e232b640ec131.jpg" alt="歌单"/><div class="cap">📜 歌单</div></div>
          <div class="shot"><img loading="lazy" src="images/a62d0758c743118ed18ef74234f1f7b3.jpg" alt="每日推荐"/><div class="cap">✨ 探索 · 每日推荐</div></div>
          <div class="shot"><img loading="lazy" src="images/a9bcd724c3a3ba7668b0c29e473b2151.jpg" alt="艺术家"/><div class="cap">🎤 探索 · 艺术家</div></div>
          <div class="shot"><img loading="lazy" src="images/c618969189f5baec852f3186c4852e3b.jpg" alt="音乐库"/><div class="cap">📚 音乐库</div></div>
        </div>
      </div>
    </div>
  </section>

  <!-- 架构 -->
  <section id="arch" class="reveal">
    <div class="sec-title">🏗️ 项目架构</div>
    <div class="sec-sub">三层结构，清清爽爽 (´｡• ᵕ •｡`)</div>
    <div class="tree">
<span class="c1">CatClawMusic/</span>
├── <span class="c2">CatClawMusic.Core/</span>          <span class="dim"># 核心层（接口 + 模型 + 服务）</span>
│   ├── <span class="c3">Interfaces/</span>             <span class="dim"># 15 个服务接口</span>
│   ├── <span class="c3">Models/</span>                <span class="dim"># 14 个数据模型</span>
│   └── <span class="c3">Services/</span>              <span class="dim"># PlayQueue / LyricsService / TagReader …</span>
│       └── <span class="c4">AI/</span>                 <span class="dim"># AgentService / AgentTools / ChatModels</span>
├── <span class="c2">CatClawMusic.Data/</span>          <span class="dim"># 数据层（数据库 + 网络 + 爬虫）</span>
│   ├── <span class="c3">MusicDatabase.cs</span>        <span class="dim"># SQLite（11 表 + 索引 + WAL）</span>
│   ├── <span class="c3">SubsonicService.cs</span>      <span class="dim"># Navidrome / OpenSubsonic</span>
│   ├── <span class="c3">WebDavService.cs</span>        <span class="dim"># WebDAV 协议</span>
│   ├── <span class="c3">SmbService.cs</span>           <span class="dim"># SMB / CIFS</span>
│   ├── <span class="c3">MusicScanner.cs</span>         <span class="dim"># 统一渐进式批量入库</span>
│   ├── <span class="c3">BackupService.cs</span>        <span class="dim"># 备份恢复（ZIP, 6 类数据）</span>
│   ├── <span class="c3">NetEaseMusicScraper.cs</span>  <span class="dim"># 网易云元数据爬虫（主）</span>
│   └── <span class="c3">AiArtistScraper.cs</span>      <span class="dim"># AI / LLM 艺术家信息</span>
└── <span class="c2">CatClawMusic.Maui/</span>          <span class="dim"># UI 层（.NET MAUI 跨平台界面）</span>
    ├── <span class="c3">AppShell.xaml</span>           <span class="dim"># Shell 导航</span>
    ├── <span class="c3">Pages/</span>                 <span class="dim"># 30+ 页面（ContentPage）</span>
    ├── <span class="c3">ViewModels/</span>            <span class="dim"># MVVM（CommunityToolkit.Mvvm）</span>
    └── <span class="c3">Platforms/Android/</span>     <span class="dim"># ExoPlayer / SAF 扫描器</span>
    </div>
    <div class="stack">
      <span>.NET 10</span><span>C# 13</span><span>MAUI 10.0.20</span><span>ExoPlayer 1.10.1</span>
      <span>CommunityToolkit.Mvvm 8.4.2</span><span>TagLibSharp 2.3.0</span><span>SQLite (sqlite-net-pcl)</span>
      <span>SMBLibrary 1.5.2</span><span>Material 3</span><span>NativeAOT</span><span>Microsoft.Extensions.DI 9.0</span>
    </div>
  </section>

  <!-- 功能 -->
  <section id="feat" class="reveal">
    <div class="sec-title">✨ 功能特性</div>
    <div class="sec-sub">很多很多，挑可爱的说 🐾</div>
    <div class="feat-grid">

      <div class="feat">
        <h3><span class="ic">🎵</span>本地音乐</h3>
        <ul>
          <li><b>SAF 文件夹选择</b>，无需 MANAGE_EXTERNAL_STORAGE</li>
          <li>多文件夹支持，权限过期自动清理</li>
          <li>MediaStore 扫描，Android 10+ 免权限</li>
          <li><b>三路径扫描策略</b>智能兜底</li>
          <li>支持 <b>26 种</b>音频格式（mp3/flac/wav/ape/dsf…）</li>
          <li>TagLibSharp 解析标题/艺术家/封面/歌词</li>
          <li><b>SAF 封面提取</b>，扫描即取嵌入封面</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">▶️</span>音频播放</h3>
        <ul>
          <li>ExoPlayer 引擎，播放/暂停/上下曲齐活</li>
          <li>流媒体 HTTP / content:// / file://</li>
          <li>WakeLock + WiFi Lock 后台不断网</li>
          <li>音频焦点智能处理（增益/恢复/降音量）</li>
          <li>播放状态自动保存，启动恢复</li>
          <li><b>音频频谱可视化</b> 64 频段实时跳动</li>
          <li>睡眠定时 10~90 分钟 + 播完再停</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🎛️</span>音效系统</h3>
        <ul>
          <li>5 频段均衡器 + 低音增强 + 环绕声</li>
          <li>混响模拟不同声场</li>
          <li><b>12 种预设</b>：杜比/音乐厅/现场/摇滚/流行/爵士…</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🔀</span>播放队列</h3>
        <ul>
          <li>顺序 / 列表循环 / 单曲循环 / 随机</li>
          <li>Fisher-Yates 洗牌，双列表设计</li>
          <li>播放历史栈，支持上一曲回退</li>
          <li>即将播放预览 + O(1) 查找</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🎶</span>歌词系统</h3>
        <ul>
          <li>LRC / TTML / AMLL 多格式解析</li>
          <li>多源歌词 + 编码自动检测防乱码</li>
          <li><b>逐字渐变高亮</b>，已唱白/未唱黑</li>
          <li>全屏毛玻璃歌词 + 拖拽定位</li>
          <li>双语歌词 + 横屏上下居中</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🖥️</span>桌面悬浮歌词</h3>
        <ul>
          <li>SYSTEM_ALERT_WINDOW 悬浮窗</li>
          <li>触摸拖拽 + 锁定模式</li>
          <li>单行跑马灯 / 双行 KTV</li>
          <li>字体·颜色·粗体·透明度全自定义</li>
          <li>通知栏快捷控制开关</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🎨</span>主题与配色</h3>
        <ul>
          <li>5 色主题 + 深/浅/跟随系统</li>
          <li><b>无重启切换</b>，音频不中断</li>
          <li>动态流光背景 + 切歌颜色过渡</li>
          <li>封面取色主题（Material You）</li>
          <li>全局导航栏隐藏，沉浸浏览</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">☁️</span>网络协议</h3>
        <ul>
          <li><b>WebDAV</b>：PROPFIND/递归/流播放</li>
          <li><b>Navidrome</b>：增量扫描/歌词/收藏同步</li>
          <li><b>SMB/CIFS</b>：共享浏览/域认证/流播放</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">📦</span>备份与恢复</h3>
        <ul>
          <li>ZIP 打包，含 JSON + 图片</li>
          <li><b>6 大数据类别</b>可单独恢复</li>
          <li>实时进度 + 跨设备自动匹配</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🔍</span>探索 · AI 对话</h3>
        <ul>
          <li>对话式卡片布局，多消息类型</li>
          <li>OpenAI 兼容 API，8 家内置供应商</li>
          <li><b>18 个 Agent 工具</b> 操控音乐</li>
          <li>猫娘人格 "Yuki" 可自定义</li>
          <li>故障转移 + 流式文本</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🔌</span>插件体系</h3>
        <ul>
          <li>5 类插件接口（歌词/封面/协议/增强/菜单）</li>
          <li>本地 / GitHub 安装，启用禁用卸载</li>
          <li>反射适配 + 子插件 + 广播联动</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🔐</span>权限管理</h3>
        <ul>
          <li>6 项权限状态一目了然</li>
          <li>通知/悬浮窗/麦克风/照片/音乐/存储</li>
          <li>一键跳转系统设置去授权</li>
        </ul>
      </div>

      <div class="feat">
        <h3><span class="ic">🖼️</span>启动页</h3>
        <ul>
          <li>自定义 API / 本地图片背景</li>
          <li>网络图片自动缓存秒开</li>
          <li>设置内实时预览 + 初始化等待</li>
        </ul>
      </div>

    </div>
  </section>

  <!-- 更新日志 -->
  <section id="changelog" class="reveal">
    <div class="sec-title">📝 更新日志</div>
    <div class="sec-sub">看看猫爪最近都在忙啥 (๑•̀ㅂ•́)و✧</div>
    <div class="log">
      <div class="ver">🐾 v1.6.4 <span class="tag new">最新</span></div>
      <ul>
        <li><span class="tag fix">修复</span>封面不显示：SAF 扫描通过 MediaMetadataRetriever 直接提取嵌入封面</li>
        <li><span class="tag fix">修复</span>进度条不动：用 ExoPlayer IPlayerListener 精准跟踪播放状态</li>
        <li><span class="tag fix">修复</span>MAUI 构建错误：Android 平台代码命名冲突</li>
        <li><span class="tag new">新功能</span>设置页完善：AI/权限/远程音乐/插件 4 页全部转正</li>
        <li><span class="tag new">新功能</span>全局导航优化 + 封面提取优化</li>
        <li><span class="tag tech">技术</span>从 Xamarin 迁移到 MAUI + CommunityToolkit.Mvvm</li>
      </ul>
    </div>
  </section>

  <!-- 数据库 -->
  <section id="db" class="reveal">
    <div class="sec-title">🗄️ 数据库结构</div>
    <div class="sec-sub">SQLite + WAL，稳稳的～</div>
    <div class="card" style="padding:0;overflow:hidden">
      <table class="tbl">
        <tr><th>项目</th><th>说明</th></tr>
        <tr><td>11 张表</td><td>Songs / Artists / Albums / SongArtists / Playlists / PlaylistSongs / Favorites / PlayHistory / Lyrics / CachedSongs / ConnectionProfiles</td></tr>
        <tr><td>多艺术家</td><td>SongArtist 多对多关联表，支持合唱</td></tr>
        <tr><td>迁移系统</td><td>数据库版本 v5，自动修复专辑关联、拆分合并艺术家</td></tr>
      </table>
    </div>
  </section>

  <!-- 协议 -->
  <section class="reveal">
    <div class="sec-title">📜 开源协议</div>
    <div class="pill-row" style="margin-top:14px">
      <span>📝 MIT License</span>
      <span>🐾 猫爪音乐 CatClaw Music</span>
      <span>💜 用 ❤️ 与 C# 打造</span>
    </div>
  </section>

</div>

<footer>
  Made with <span class="heart">♥</span> by 猫爪音乐 · 🐾 喵～ 感谢你来看这只小猫的音乐世界
</footer>

<script>
  // 滚动揭示
  const io = new IntersectionObserver((es)=>{
    es.forEach(e=>{ if(e.isIntersecting){ e.target.classList.add('in'); io.unobserve(e.target);} });
  },{threshold:.08});
  document.querySelectorAll('.reveal').forEach(el=>io.observe(el));

  // 截图点击放大
  document.querySelectorAll('.shot img').forEach(img=>{
    img.style.cursor='zoom-in';
    img.addEventListener('click',()=>{
      const w=window.open(img.src,'_blank');
      if(!w) location.href=img.src;
    });
  });

  // 截图区默认收起，点击标题展开/收起
  const shotsHead=document.getElementById('shotsHead');
  const shotsWrap=document.getElementById('shotsWrap');
  shotsHead.addEventListener('click',()=>{
    const open=shotsWrap.classList.toggle('open');
    shotsHead.classList.toggle('open',open);
    shotsHead.setAttribute('aria-expanded',open);
  });
</script>
</body>
</html>
