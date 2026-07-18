# Android APK 签名信息

> 此文件仅供开发参考，开源项目可公开。

| 项目 | 值 |
|------|-----|
| 文件 | `catclaw.keystore`（项目根目录） |
| 格式 | PKCS12，2048-bit RSA |
| 别名 (alias) | `catclaw` |
| 密码 | `catclaw123`（storepass = keypass） |
| 有效期 | 至 2053 年（约 27 年） |

## Release 编译命令

```bash
dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj -f net11.0-android -c Release
```

csproj 中已配置签名引用 `..\catclaw.keystore`，密码通过 MSBuild 属性传入。

若未设置环境变量，需显式指定密码：

```bash
dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj -f net11.0-android -c Release -p:CatClawStorePass="catclaw123" -p:CatClawKeyPass="catclaw123"
```

输出 APK 路径：`CatClawMusic.Maui\bin\Release\net11.0-android\com.catclaw.music-Signed.apk`
