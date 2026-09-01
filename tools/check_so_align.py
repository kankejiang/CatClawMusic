"""
临时脚本：检查 APK 内 native .so 是否为 16KB 页对齐（Android 15+/16KB 页设备要求）。
用法: python tools/check_so_align.py <apk路径>
"""
import struct
import sys
import zipfile


def parse_elf_alignment(data: bytes) -> list[tuple[int, int]]:
    """返回 ELF 程序头中 LOAD 段的 (p_offset, p_align)。"""
    if data[:4] != b"\x7fELF":
        return []
    is64 = data[4] == 2
    if is64:
        e_phoff = struct.unpack_from("<Q", data, 0x20)[0]
        e_phentsize = struct.unpack_from("<H", data, 0x36)[0]
        e_phnum = struct.unpack_from("<H", data, 0x38)[0]
        out = []
        for i in range(e_phnum):
            off = e_phoff + i * e_phentsize
            p_type = struct.unpack_from("<I", data, off)[0]
            if p_type != 1:  # PT_LOAD
                continue
            p_offset = struct.unpack_from("<Q", data, off + 0x08)[0]
            p_align = struct.unpack_from("<Q", data, off + 0x30)[0]  # Phdr64: p_align @ 0x30
            out.append((p_offset, p_align))
        return out
    else:
        e_phoff = struct.unpack_from("<I", data, 0x1C)[0]
        e_phentsize = struct.unpack_from("<H", data, 0x2A)[0]
        e_phnum = struct.unpack_from("<H", data, 0x2C)[0]
        out = []
        for i in range(e_phnum):
            off = e_phoff + i * e_phentsize
            p_type = struct.unpack_from("<I", data, off)[0]
            if p_type != 1:
                continue
            p_offset = struct.unpack_from("<I", data, off + 0x04)[0]
            p_align = struct.unpack_from("<I", data, off + 0x1C)[0]
            out.append((p_offset, p_align))
        return out


def main() -> None:
    if len(sys.argv) < 2:
        print("用法: python tools/check_so_align.py <apk>")
        sys.exit(1)

    apk = sys.argv[1]
    total = 0
    bad: list[str] = []
    with zipfile.ZipFile(apk) as z:
        for info in z.infolist():
            if not info.filename.startswith("lib/") or not info.filename.endswith(".so"):
                continue
            total += 1
            name = info.filename
            with z.open(info) as f:
                head = f.read(4096)
            loads = parse_elf_alignment(head)
            if not loads:
                bad.append(f"{name}: 非 ELF/无法解析")
                continue
            max_align = max(a for _, a in loads)
            bad_align = [o for o, a in loads if a < 16384]
            if max_align < 16384 or bad_align:
                bad.append(f"{name}: p_align={max_align}, 非对齐段偏移={bad_align[:3]}")

    print(f"=== total .so = {total}, 16KB-misaligned = {len(bad)} ===")
    for line in bad[:20]:
        print("  ", line)


if __name__ == "__main__":
    main()
