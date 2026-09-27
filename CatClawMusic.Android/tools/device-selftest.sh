#!/usr/bin/env bash
# CatClawMusic 安卓端真机自测 + §3 判据取数脚本（清单 §2 M1「真机自测脚本」与 §Verification 2/3 的落地）。
#
# 为什么要有这个文件：这些步骤此前每次都是手敲 adb 串起来的，而手敲有两个固定代价 ——
# 换设备/换包名要重推一遍，以及"跑了一半忘了上一步量的是什么"。
#
# 诚实原则：需要人做或缺外部条件的步骤（断网续播、MAUI 侧手点一次 ⏭ 取切歌基线）
# 一律打 NEEDS_MANUAL 并说明缺什么，**不拿 Kotlin 侧的数字冒充对比结论**。
#
# 用法：
#   tools/device-selftest.sh                          # 自动挑第一台在线设备，debug 包
#   tools/device-selftest.sh -s 93ea7079
#   tools/device-selftest.sh --variant release        # 包名换成 com.catclaw.music.kt
#   tools/device-selftest.sh --dry-run                # 只打印步骤，不碰设备
set -uo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
MODULE_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)

VARIANT=debug
PKG=com.catclaw.music.kt.debug
ACTIVITY=com.catclaw.music.kt.MainActivity
APK=""
SERIAL=""
DRY=0
COLD_RUNS=5

usage() {
  cat <<'EOF'
用法: device-selftest.sh [-s 序列号] [--variant debug|release] [--apk 路径] [--dry-run]
  --variant release  包名换成 com.catclaw.music.kt（无 .debug 后缀）
  步骤：安装 → 冷启动 N 次取中位 → 扫描/队列/模式/切歌 → 杀进程恢复 → PSS → 词库链路 → 收尾卫生
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -s) SERIAL=$2; shift 2 ;;
    --variant) VARIANT=$2; shift 2 ;;
    --apk) APK=$2; shift 2 ;;
    --dry-run) DRY=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "未知参数: $1"; usage; exit 2 ;;
  esac
done

if [[ $VARIANT == release ]]; then
  PKG=com.catclaw.music.kt
  [[ -z $APK ]] && APK=$MODULE_ROOT/app/build/outputs/apk/release/app-release.apk
else
  [[ -z $APK ]] && APK=$MODULE_ROOT/app/build/outputs/apk/debug/app-debug.apk
fi

# adb 经常不在 PATH 里（本机就是），按 SDK 默认位置兜底。
# 注意 Git Bash 不一定导出 USER，`set -u` 下必须走 :- 兜底，否则脚本在这里就炸。
ADB=$(command -v adb || true)
if [[ -z $ADB ]]; then
  for c in "${LOCALAPPDATA:-}/Android/Sdk/platform-tools/adb.exe" \
           "${HOME:-}/AppData/Local/Android/Sdk/platform-tools/adb.exe" \
           "/c/Users/${USERNAME:-${USER:-}}/AppData/Local/Android/Sdk/platform-tools/adb.exe"; do
    [[ -n $c && -x $c ]] && ADB=$c && break
  done
fi

N_PASS=0 N_FAIL=0 N_MANUAL=0
ok()    { N_PASS=$((N_PASS+1));   printf '  [OK]     %s\n' "$1"; }
bad()   { N_FAIL=$((N_FAIL+1));   printf '  [FAIL]   %s\n' "$1"; }
manual(){ N_MANUAL=$((N_MANUAL+1)); printf '  [需人工] %s\n' "$1"; }
section(){ printf '\n== %s\n' "$1"; }

run() {
  if [[ $DRY -eq 1 ]]; then return 0; fi
  "$@"
}

median_of() {
  sort -n | awk '{a[NR]=$1} END{ if (NR==0) {print "n/a"; exit}
    if (NR%2) print a[(NR+1)/2]; else printf "%.1f", (a[NR/2]+a[NR/2+1])/2 }'
}

# ---------------------------------------------------------------- 0 设备与产物
section "0. 设备与产物"
if [[ $DRY -eq 0 ]]; then
  [[ -n $ADB && -x $ADB ]] || { echo "找不到 adb；装 platform-tools 或把 adb 放进 PATH"; exit 3; }
  [[ -z $SERIAL ]] && SERIAL=$("$ADB" devices | awk 'NR>1 && $2=="device"{print $1; exit}')
  [[ -n $SERIAL ]] || { echo "没有在线设备。当前：$("$ADB" devices | tail -n +2 | tr -d '\r')"; exit 3; }
  SDK_LVL=$("$ADB" -s "$SERIAL" shell getprop ro.build.version.sdk | tr -d '\r')
  ABI=$("$ADB" -s "$SERIAL" shell getprop ro.product.cpu.abi | tr -d '\r')
  RES=$("$ADB" -s "$SERIAL" shell wm size | tr -d '\r')
  echo "  设备=$SERIAL API=$SDK_LVL ABI=$ABI $RES  包=$PKG"
  # 基线口径：arm64 真机与 x86_64 模拟器上的数字不可互相比较（清单里记过一次）
  case $ABI in
    arm64*) ok "arm64 真机：可与 MAUI 基线同机对比" ;;
    *)      manual "ABI=$ABI 不是 arm64 真机；MAUI 基线取自 arm64，数字别混用" ;;
  esac
  if [[ -f $APK ]]; then ok "APK $(stat -c%s "$APK") B（$(basename "$APK")）"
  else bad "APK 不存在：$APK（先 ./gradlew :app:assemble$VARIANT）"; exit 3; fi
else
  echo "  (dry-run) 跳过设备探测，APK 目标 $APK"
fi

# ---------------------------------------------------------------- 1 安装
section "1. 安装（MIUI 在锁屏下会直接 INSTALL_FAILED_USER_RESTRICTED 且不弹框，先唤醒解锁）"
if [[ $DRY -eq 0 ]]; then
  run "$ADB" -s "$SERIAL" shell input keyevent KEYCODE_WAKEUP
  run "$ADB" -s "$SERIAL" shell input swipe 700 2400 700 900 200
  OUT=$("$ADB" -s "$SERIAL" install -r -t "$APK" 2>&1 | tail -1)
  [[ $OUT == *Success* ]] && ok "install -r -t 成功" || bad "安装失败：$OUT"
fi

# ---------------------------------------------------------------- 2 冷启动
section "2. §3 冷启动 TotalTime ×$COLD_RUNS（取中位；release 与 debug 口径不可互比）"
if [[ $DRY -eq 0 ]]; then
  SAMPLES=$(mktemp)
  for i in $(seq 1 "$COLD_RUNS"); do
    run "$ADB" -s "$SERIAL" shell am force-stop "$PKG"
    sleep 2
    T=$("$ADB" -s "$SERIAL" shell am start -W -n "$PKG/$ACTIVITY" 2>&1 | tr -d '\r' | sed -n 's/^TotalTime: //p')
    if [[ -z $T ]]; then
      # 设备打盹/锁屏时这一行整条不输出：量到的是"没有数"，不是"很快"
      manual "第 $i 次没拿到 TotalTime（设备可能在锁屏/打盹）；唤醒后重跑，空值不能当中位数"
    else
      echo "$T" >> "$SAMPLES"; echo "  第 $i 次 ${T}ms"
    fi
  done
  GOT=$(wc -l < "$SAMPLES")
  MED=$(median_of < "$SAMPLES")
  if [[ $GOT -lt $COLD_RUNS ]]; then
    # 样本不全时中位数不可信：设备在打盹/锁屏的那几次不是"很快"而是"没量到"，
    # 混进来会把中位数拉偏（实测：同一台机器正常 1193ms，打盹时同法量到 2488ms）。
    bad "冷启动样本不全（$GOT/$COLD_RUNS），中位数 $MED ms 仅作废弃参考，不要记录"
  else
    ok "冷启动中位 $MED ms（$GOT 次，全部取到）"
  fi
  rm -f "$SAMPLES"
  FATAL=$("$ADB" -s "$SERIAL" shell logcat -d 2>&1 | grep -c "FATAL EXCEPTION")
  if [[ $FATAL == 0 ]]; then ok "启动后 FATAL 计数 0"; else bad "logcat 里有 $FATAL 次 FATAL EXCEPTION"; fi
fi

# ---------------------------------------------------------------- 3 M1 自测
section "3. M1 自测：扫描入库 → 队列 → 模式循环 → 切歌 → 杀进程恢复"
if [[ $DRY -eq 1 ]]; then
  echo "  (dry-run) 下面 5 步会跑：扫描/模式/切歌/锁屏/杀进程恢复，其中 4 步需人工点"
else
  # 基线要在清日志之前取，否则等于把自己要对比的东西擦掉了
  BEFORE=$("$ADB" -s "$SERIAL" logcat -d -s CatClawPlayback:V 2>&1 | grep -o 'size=[0-9]*' | tail -1)
  run "$ADB" -s "$SERIAL" shell logcat -c
  manual "扫描入库：点一次「扫描音乐」，看 scanned=/inserted=/elapsed=（脚本不猜坐标）"
  manual "模式循环：顺序→随机→单曲循环→列表循环各跑一轮，mode= 要真的换过"
  manual "切歌延迟：点 ⏭ 后 state song= 要变；MAUI 侧同曲库同机取一次基线才能算差值"
  manual "锁屏切歌：KEYCODE_MEDIA_NEXT 之后 playing=true 要保住"
  manual "断网续播：只对远程音源有意义（属 M5），本地文件没有这条路径"
  run "$ADB" -s "$SERIAL" shell am force-stop "$PKG"
  run "$ADB" -s "$SERIAL" shell am start -n "$PKG/$ACTIVITY" >/dev/null
  sleep 5
  AFTER=$("$ADB" -s "$SERIAL" logcat -d -s CatClawPlayback:V 2>&1 | grep -o 'size=[0-9]*' | tail -1)
  if [[ -n $BEFORE && $BEFORE == "$AFTER" ]]; then ok "杀进程重启后队列恢复 $AFTER"
  else manual "队列对不上（重启前 ${BEFORE:-无} / 重启后 ${AFTER:-无}）—— 先确认重启前真入过队"; fi
fi

# ---------------------------------------------------------------- 4 PSS
section "4. §3 PSS（同工况才可比：空闲 vs 空闲、播放 30min vs 播放 30min）"
if [[ $DRY -eq 0 ]]; then
  MI=$("$ADB" -s "$SERIAL" shell dumpsys meminfo "$PKG" 2>&1 | tr -d '\r')
  # 行首有一堆空格、同一行还跟着 TOTAL RSS / TOTAL SWAP PSS —— 只能按字段名取，`^TOTAL` 匹配不上
  PSS=$(echo "$MI" | sed -n 's/.*TOTAL PSS: *\([0-9]*\).*/\1/p' | head -1)
  SWAP=$(echo "$MI" | sed -n 's/.*TOTAL SWAP PSS: *\([0-9]*\).*/\1/p' | head -1)
  echo "  ${PKG} TOTAL PSS = ${PSS:-n/a} KB，SWAP PSS = ${SWAP:-0} KB"
  # 只比 PSS 会低估被大量换出的那个进程：真实占用要把 swap 加回去，两边同口径才可比
  [[ -n $PSS ]] && echo "  → 含换出的真实占用 = $((PSS + ${SWAP:-0})) KB"
  manual "MAUI 同工况基线：force-stop com.catclaw.music → 冷启动 → 静置同样时长 → 同一条命令取数"
fi

# ---------------------------------------------------------------- 5 词库链路
section "5. M6 词库真机链路（回复不该是「AI 还没配置」那句）"
if [[ $DRY -eq 1 ]]; then
  echo "  (dry-run) 发现页滚到底 → 点「问问 Yuki」→ 输入 hello → 发送 → 读气泡"
else
  manual "按 uiautomator bounds 点：dump 后取 text 含「问问 Yuki」的节点中心（别从截图反算倍数）"
  manual "判据：回复文本能在 wordlib.db 里反查到原行（{me} 会替换成 yuki）即算验到"
fi

# ---------------------------------------------------------------- 6 卫生
section "6. 收尾卫生"
if [[ $DRY -eq 0 ]]; then
  if [[ $VARIANT == release ]]; then
    manual "release 包是 debug key 临时签的：验完 adb -s $SERIAL uninstall $PKG，否则将来正式签名同 id 会 INSTALL_FAILED_UPDATE_INCOMPATIBLE"
  fi
  manual "git status --porcelain 只应出现 CatClawMusic.Android/、tools/*.py、用户的 .idea/"
fi

section "结果"
printf '  自动通过 %d 项 / 失败 %d 项 / 需人工或缺外部条件 %d 项\n' "$N_PASS" "$N_FAIL" "$N_MANUAL"
[[ $N_FAIL -gt 0 ]] && exit 1
exit 0
