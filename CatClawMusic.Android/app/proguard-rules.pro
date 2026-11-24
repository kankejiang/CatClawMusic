# Room 的 @Database 实现类由框架按名字反射实例化
-keep class * extends androidx.room.RoomDatabase { <init>(); }
-dontwarn androidx.room.paging.**

# Hilt 的组件在编译期生成、运行期按名字加载
-keep class dagger.hilt.** { *; }
-keep class javax.inject.** { *; }

# Media3 按类名反射创建 Extractor / Decoder
-keep class androidx.media3.exoplayer.** { *; }
-dontwarn androidx.media3.**

# okhttp 5 的平台桥接按 Android 版本反射选择
-keep class okhttp3.internal.platform.** { *; }
-dontwarn okhttp3.internal.platform.**
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**

# SMBJ 的两条可选路径在安卓上根本不存在，只会让 R8 报缺类：
# mbassador 的 EL 表达式集成（javax.el，我们不用），以及 smbj 的 Kerberos/SPNEGO（org.ietf.jgss，
# 安卓没有 JGSS，实际走 NTLM）。裁掉即可，但注意这也意味着安卓侧不支持 Kerberos 认证。
-dontwarn javax.el.**
-dontwarn org.ietf.jgss.**

# ─────────────────────────── QuickJS 插件宿主 API ───────────────────────────
#
# app.cash.quickjs 的 `QuickJs.set(name, Class<T>, impl)` 在运行时校验：
# **type 必须是 interface**，否则抛
#   "Only interfaces can be bound. Received: class ye2"
#
# R8 默认会把只有一个实现的方法/接口做「接口→类」优化（把 LyricoHostApi 合并进
# 实现类，或直接改成 class），校验就失败了。
#
# 真机表现（红米 M332BF / Android 17，release 包）：**所有插件源装载失败，
# 搜索只剩 LRCLIB**，而 debug 包（不混淆）一切正常 —— 这正是 R8 混淆导致的。
#
# 必须保留：
# 1. 接口本身仍是 interface（-keep interface 明确表达这一点）
# 2. 方法签名不被裁掉（QuickJS 按反射查方法）
-keep interface com.catclaw.music.kt.lyrico.plugin.LyricoHostApi { *; }
-keep class * implements com.catclaw.music.kt.lyrico.plugin.LyricoHostApi { *; }

# QuickJS 自身：JNI 按类名/方法名反射调用，混淆后必崩
-keep class app.cash.quickjs.** { *; }
-dontwarn app.cash.quickjs.**
