# Android APK 签名信息

> 此文件仅供开发参考，开源项目可公开。

| 项目 | 值 |
|------|-----|
| 文件 | `catclaw.keystore`（项目根目录） |
| 格式 | PKCS12，2048-bit RSA |
| 别名 (alias) | `catclaw` |
| 密码 | `catclaw123`（storepass = keypass） |
| 有效期 | 至 2053 年（约 27 年） |

## Release 编译命令（出签名包）

> ⚠️ 项目已适配 MAUI 10，TargetFramework 为 `net10.0-android`（旧文档的 `net11` 已失效，本环境 net11 无法编译）。

```bash
dotnet publish CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Release -f net10.0-android ^
  -p:AndroidSdkDirectory="C:/Users/Administrator/AppData/Local/Android/Sdk" ^
  -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.11.10-hotspot/" ^
  -p:CatClawStorePass="catclaw123" -p:CatClawKeyPass="catclaw123"
```

csproj 中已配置签名引用 `..\catclaw.keystore`，密码通过 MSBuild 属性传入。

若未设置环境变量，需显式指定密码（同上）。

输出 APK 路径：`CatClawMusic.Maui\bin\Release\net10.0-android\publish\com.catclaw.music-Signed.apk`
