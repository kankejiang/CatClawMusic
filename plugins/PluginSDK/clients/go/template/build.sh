#!/bin/bash
# ──────────────────────────────────────────────────────
# 猫爪音乐 Go 插件编译脚本
#
# 用法:
#   ./build.sh              # 编译 iOS/Android/Linux (arm64)
#   ./build.sh android      # 仅编译 Android
#   ./build.sh linux        # 仅编译 Linux (amd64)
#
# 产物:
#   libgoplugin.so (Android/Linux) 或 goplugin.dll (Windows)
# ──────────────────────────────────────────────────────

set -e

PLUGIN_DIR="$(cd "$(dirname "$0")" && pwd)"
GO_SRC="main.go"

build_android() {
    local arch="$1"
    local goarch="$2"
    local cc="$3"

    echo "  [Android/${arch}] Building..."
    GOOS=android GOARCH="${goarch}" CGO_ENABLED=1 \
        CC="${cc}" \
        go build -buildmode=c-shared -o "libgoplugin_${arch}.so" "${GO_SRC}"
    echo "  [Android/${arch}] Done: libgoplugin_${arch}.so"
}

build_linux() {
    echo "  [Linux/amd64] Building..."
    CGO_ENABLED=1 go build -buildmode=c-shared -o libgoplugin.so "${GO_SRC}"
    echo "  [Linux/amd64] Done: libgoplugin.so"
}

build_windows() {
    echo "  [Windows/amd64] Building..."
    CGO_ENABLED=1 go build -buildmode=c-shared -o goplugin.dll "${GO_SRC}"
    echo "  [Windows/amd64] Done: goplugin.dll"
}

echo "=============================="
echo " CatClawMusic Go Plugin Build"
echo "=============================="

cd "${PLUGIN_DIR}"

case "${1:-all}" in
    android)
        build_android "arm64" "arm64" "${ANDROID_NDK_HOME}/toolchains/llvm/prebuilt/linux-x86_64/bin/aarch64-linux-android21-clang"
        ;;
    linux)
        build_linux
        ;;
    windows)
        build_windows
        ;;
    all)
        build_linux
        echo ""
        echo "NOTE: Android target requires ANDROID_NDK_HOME environment variable."
        echo "      Run './build.sh android' after setting it."
        ;;
    *)
        echo "Usage: $0 [android|linux|windows|all]"
        exit 1
        ;;
esac

echo ""
echo "=========================================="
echo " Next step: dotnet build → .ccp"
echo "=========================================="
