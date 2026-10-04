"""Dump menus, string tables, accelerators and dialog captions from SketchUp.exe."""
import json
import struct
import sys

import pefile

EXE = sys.argv[1]
OUT = sys.argv[2]

pe = pefile.PE(EXE, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_RESOURCE"]])


def entries(type_id):
    for t in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if t.id == type_id:
            for e in t.directory.entries:
                for lang in e.directory.entries:
                    rva, size = lang.data.struct.OffsetToData, lang.data.struct.Size
                    yield (e.id if e.id is not None else str(e.name)), pe.get_data(rva, size)


def read_wstr(data, off):
    end = off
    while data[end:end + 2] != b"\0\0":
        end += 2
    return data[off:end].decode("utf-16-le"), end + 2


# --- string tables: block id N holds strings (N-1)*16 .. (N-1)*16+15
strings = {}
for block, data in entries(6):
    off = 0
    for i in range(16):
        (n,) = struct.unpack_from("<H", data, off)
        off += 2
        if n:
            strings[(block - 1) * 16 + i] = data[off:off + n * 2].decode("utf-16-le")
        off += n * 2


# --- menus (classic MENUTEMPLATE and MENUEX)
def parse_menu(data):
    ver, hdr = struct.unpack_from("<HH", data, 0)
    if ver == 1:
        return parse_menuex(data, 4 + hdr)[0]
    items, _ = parse_items(data, 4)
    return items


def parse_items(data, off):
    items = []
    while True:
        (flags,) = struct.unpack_from("<H", data, off)
        off += 2
        cmd = None
        if not flags & 0x10:
            (cmd,) = struct.unpack_from("<H", data, off)
            off += 2
        text, off = read_wstr(data, off)
        item = {"text": text, "id": cmd}
        if flags & 0x10:
            item["children"], off = parse_items(data, off)
        items.append(item)
        if flags & 0x80:
            return items, off


def parse_menuex(data, off):
    items = []
    while True:
        _type, _state, cmd, flags = struct.unpack_from("<IIIH", data, off)
        off += 14
        text, off = read_wstr(data, off)
        off = (off + 3) & ~3
        item = {"text": text, "id": cmd}
        if flags & 0x01:
            off += 4  # help id
            item["children"], off = parse_menuex(data, off)
        items.append(item)
        if flags & 0x80:
            return items, off


menus = {}
for mid, data in entries(4):
    try:
        menus[str(mid)] = parse_menu(data)
    except Exception as exc:  # noqa: BLE001
        menus[str(mid)] = f"parse error: {exc}"

# --- accelerators
VK = {0x08: "Backspace", 0x09: "Tab", 0x0D: "Enter", 0x1B: "Esc", 0x20: "Space", 0x21: "PgUp", 0x22: "PgDn",
      0x23: "End", 0x24: "Home", 0x25: "Left", 0x26: "Up", 0x27: "Right", 0x28: "Down", 0x2D: "Insert", 0x2E: "Delete"}
accels = []
for aid, data in entries(9):
    for off in range(0, len(data), 8):
        fvirt, key, cmd, _ = struct.unpack_from("<HHHH", data, off)
        mods = [m for bit, m in ((0x08, "Ctrl"), (0x10, "Alt"), (0x04, "Shift")) if fvirt & bit]
        if fvirt & 0x01:
            k = VK.get(key) or (f"F{key - 0x6F}" if 0x70 <= key <= 0x87 else chr(key) if 32 < key < 127 else hex(key))
        else:
            k = chr(key)
        accels.append({"table": aid, "keys": "+".join(mods + [k]), "id": cmd, "cmd": strings.get(cmd, "")})

# --- dialog captions
dialogs = {}
for did, data in entries(5):
    try:
        if struct.unpack_from("<HH", data, 0) == (1, 0xFFFF):
            off = 26
        else:
            off = 18
        for _ in range(2):  # menu, class
            (w,) = struct.unpack_from("<H", data, off)
            if w == 0xFFFF:
                off += 4
            elif w == 0:
                off += 2
            else:
                _, off = read_wstr(data, off)
        title, _ = read_wstr(data, off)
        dialogs[str(did)] = title
    except Exception as exc:  # noqa: BLE001
        dialogs[str(did)] = f"parse error: {exc}"

json.dump({"menus": menus, "strings": strings, "accelerators": accels, "dialogs": dialogs},
          open(OUT, "w"), indent=1, ensure_ascii=False)
print(len(menus), "menus,", len(strings), "strings,", len(accels), "accels,", len(dialogs), "dialogs")
