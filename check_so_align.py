import zipfile, struct

apk = r"c:\Code\CatClawMusic\CatClawMusic.Maui\bin\Release\net10.0-android\publish\com.catclaw.music-Signed.apk"
z = zipfile.ZipFile(apk)
bad = []
so_count = 0
for n in z.namelist():
    if n.startswith("lib/") and n.endswith(".so"):
        so_count += 1
        data = z.read(n)
        if data[:4] != b"\x7fELF":
            bad.append((n, "NOT ELF"))
            continue
        ei_class = data[4]
        if ei_class == 2:  # 64-bit
            e_phoff = struct.unpack_from("<Q", data, 0x20)[0]
            e_phentsize = struct.unpack_from("<H", data, 0x36)[0]
            e_phnum = struct.unpack_from("<H", data, 0x38)[0]
        else:
            e_phoff = struct.unpack_from("<I", data, 0x1c)[0]
            e_phentsize = struct.unpack_from("<H", data, 0x2a)[0]
            e_phnum = struct.unpack_from("<H", data, 0x2c)[0]
        max_align = 0
        for i in range(e_phnum):
            off = e_phoff + i * e_phentsize
            p_type = struct.unpack_from("<I", data, off)[0]
            if p_type == 1:  # PT_LOAD
                if ei_class == 2:
                    p_align = struct.unpack_from("<Q", data, off + 0x30)[0]
                else:
                    p_align = struct.unpack_from("<I", data, off + 0x1c)[0]
                max_align = max(max_align, p_align)
        if max_align < 0x4000:
            bad.append((n, f"max_align=0x{max_align:x} (need 0x4000)"))

print(f"=== total .so = {so_count}, 16KB-misaligned = {len(bad)} ===")
for n, a in bad:
    print(n, "->", a)
