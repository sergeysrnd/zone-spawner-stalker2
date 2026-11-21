"""Generate Zone Kit item generators from the public Zone Spawner catalog."""

import argparse
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
CFG_PATH = Path("Content/GameLite/GameData/ItemGeneratorPrototypes/ZoneSpawnerSupply.cfg")
ITEM_CATEGORIES = {"Патроны": "Ammo", "Расходники": "Consumable"}
VALID_SID = re.compile(r"[A-Za-z][A-Za-z0-9_]{0,127}")


def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate kit or item SID: {key}")
        result[key] = value
    return result


def render_kits(kits, catalog):
    if not isinstance(kits, dict) or not kits:
        raise ValueError("Define at least one supply kit.")

    lines = []
    for kit_sid, items in kits.items():
        if not VALID_SID.fullmatch(kit_sid) or not kit_sid.startswith("ZoneSpawnerSupply"):
            raise ValueError(f"Invalid kit SID: {kit_sid}")
        if not isinstance(items, dict) or not items:
            raise ValueError(f"{kit_sid}: define at least one item.")

        groups = []
        categories = set()
        for sid, count in items.items():
            item = catalog.get(sid)
            if not VALID_SID.fullmatch(sid) or not item or item.get("Restricted") is not False:
                raise ValueError(f"{kit_sid}: unknown or restricted item {sid}.")
            category = ITEM_CATEGORIES.get(item.get("Category"))
            if not category:
                raise ValueError(f"{kit_sid}: unsupported item category for {sid}.")
            if category in categories:
                raise ValueError(f"{kit_sid}: use one item per generation category.")
            if type(count) is not int or not 1 <= count <= (100 if category == "Ammo" else 5):
                raise ValueError(f"{kit_sid}: invalid quantity for {sid}.")
            categories.add(category)
            groups.append((category, sid, count))

        lines.extend([f"{kit_sid} : struct.begin", f"   SID = {kit_sid}", "   ItemGenerator : struct.begin"])
        for index, (category, sid, count) in enumerate(groups):
            lines.extend([
                f"      [{index}] : struct.begin",
                f"         Category = EItemGenerationCategory::{category}",
                "         PossibleItems : struct.begin",
                "            [0] : struct.begin",
                f"               ItemPrototypeSID = {sid}",
                "               Chance = 1.0",
                "               Weight = 1.0",
                f"               MinCount = {count}",
                f"               MaxCount = {count}",
            ])
            if category == "Ammo":
                lines.append("               bRequireWeapon = false")
            lines.extend(["            struct.end", "         struct.end", "      struct.end"])
        lines.extend(["   struct.end", "struct.end", ""])

    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kits", type=Path, default=HERE / "supply-kits.json")
    parser.add_argument("--output", type=Path, default=REPO / "build/zone-kit",
                        help="Staging folder for the generated Content directory.")
    args = parser.parse_args()
    try:
        kits = json.loads(args.kits.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_keys)
        records = json.loads((REPO / "items.json").read_text(encoding="utf-8-sig"))
        contents = render_kits(kits, {item["Id"]: item for item in records})
        target = args.output / CFG_PATH
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("w", encoding="utf-8", newline="\n") as file:
            file.write(contents)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    print(f"Generated {len(kits)} supply kits: {target}")


if __name__ == "__main__":
    main()
