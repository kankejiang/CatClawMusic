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
