# 第三方代码来源与许可

本模块是**移植/内嵌**进来的第三方代码，不是在本仓库原创。改动点见各文件头注释与
`CatClawMusic.Android` 的提交说明。发布二进制时需一并满足下列许可的署名要求。

## Lyrico（Apache License 2.0）

- 上游：https://github.com/Replica0110/Lyrico
- 取用范围：`lyrico-audiotag` 模块的 Kotlin 绑定与 `src/main/cpp/*.cpp` JNI 封装
  （`TagLib.kt`、`AudioTagReader/Writer`、`model/*`、`FdUtils`、`taglib.cpp`、`fileref_ext.cpp`）。
- 本地改动：NDK/CMake 版本对齐本工程、ABI 与本工程一致、去掉 `Parcelable`、
  `CMakeLists.txt` 去掉 objcopy 二次裁剪。
- 许可全文见仓库根 `LICENSE.txt` 之外的 Apache-2.0 文本
  （https://www.apache.org/licenses/LICENSE-2.0）。按 Apache-2.0 §4 需保留版权与许可声明、
  并在修改过的文件中说明改动（已在文件头注明）。

## TagLib（MPL-1.1 或 LGPL-2.1 双许可）

- 上游：https://github.com/taglib/taglib （vendored 版本 v2.0.2，位于 `src/main/cpp/taglib/`）
- 本项目**选择 MPL-1.1**，以规避 LGPL 对静态/动态链接的再链接义务。
- 许可全文见 `src/main/cpp/taglib/COPYING.MPL`（另有 `COPYING.LGPL` 供参考）。
- TagLib 以独立共享库 `libtaglib.so` 形式随 APK 分发，未修改其源码。

## utfcpp（Boost Software License 1.0）

- 上游：https://github.com/nemtrif/utfcpp （vendored 于 `src/main/cpp/taglib/3rdparty/utfcpp/`）
- TagLib 构建期头文件依赖，许可全文见该目录 `LICENSE`。

## QuickJS（MIT）

- 通过 Maven 依赖 `app.cash.quickjs:quickjs-android` 引入（Apache-2.0 打包，内含 QuickJS 引擎）。
- 上游：https://github.com/cashapp/quickjs-java ，QuickJS 本体 https://bellard.org/quickjs/
