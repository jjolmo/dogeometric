"""Build app/data/sketchup_commands.json from research/sketchup_resources.json.

Keeps the main menu tree (resource 127), each command's label, status-bar description and tooltip,
and SketchUp's default shortcuts (accelerator tables 127 and 157).
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
src = json.loads((ROOT / "research" / "sketchup_resources.json").read_text())
strings = {int(k): v for k, v in src["strings"].items()}


def clean(items):
    out = []
    for it in items:
        text = it["text"]
        if not text and not it.get("children"):
            out.append({"separator": True})
            continue
        label, _, accel = text.partition("\t")
        node = {"label": label.replace("&", "").strip()}
        if it.get("children"):
            node["children"] = clean(it["children"])
        else:
            node["id"] = it["id"]
        out.append(node)
    return out


menus = clean(src["menus"]["127"])

def menu_ids(items):
    for it in items:
        if "id" in it:
            yield it["id"]
        yield from menu_ids(it.get("children", []))


# Only the strings the menus need: the status-bar description of each menu command.
commands = {}
for cid in sorted(set(menu_ids(menus))):
    desc = strings.get(cid, "").partition("\n")[0]
    if desc:
        commands[str(cid)] = {"description": desc}

# Table 157 (tool letters) wins over 127 for the label shown in menus, matching SketchUp's menus.
shortcuts = []
for table in ("157", "127"):
    for a in src["accelerators"]:
        if str(a["table"]) == table:
            shortcuts.append({"keys": a["keys"], "id": a["id"]})

out = ROOT / "app" / "data" / "sketchup_commands.json"
out.write_text(json.dumps({"menus": menus, "commands": commands, "shortcuts": shortcuts}, indent=1, ensure_ascii=False))
print(f"wrote {out} ({len(menus)} menus, {len(commands)} strings, {len(shortcuts)} shortcuts)")
