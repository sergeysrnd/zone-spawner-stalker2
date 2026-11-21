"""Check loose Zone Kit files and PAK listings for loader and executable files.

This check does not inspect IoStore contents or verify in-game behavior.
"""

import argparse
import json
import subprocess
from pathlib import Path, PurePosixPath


BLOCKED_SUFFIXES = {
    ".asi", ".bat", ".cmd", ".com", ".cpl", ".dll", ".exe", ".jse", ".js", ".lnk",
    ".lua", ".luac", ".msi", ".ocx", ".ps1", ".psm1", ".py", ".pyc", ".reg",
    ".scr", ".sys", ".vbe", ".vbs", ".wsf",
}
LOADER_NAMES = ("ue4ss", "zonespawnerlauncher")


def names_loader(name):
    return any(loader in name.casefold() for loader in LOADER_NAMES)


def check_path(name, location):
    path = PurePosixPath(name.replace("\\", "/").rstrip(" ."))
    if names_loader(str(path)) or path.suffix.casefold() in BLOCKED_SUFFIXES:
        raise ValueError(f"Unexpected loader, executable or script: {location}")
    return path.suffix.casefold()


def check_descriptor(contents, location):
    try:
        descriptor = json.loads(contents)
    except ValueError as error:
        raise ValueError(f"Cannot read plugin descriptor {location}: {error}") from error
    if not isinstance(descriptor, dict):
        raise ValueError(f"Invalid plugin descriptor: {location}")
    if descriptor.get("Modules"):
        raise ValueError(f"Plugin descriptor declares native modules: {location}")
    plugins = descriptor.get("Plugins", [])
    if not isinstance(plugins, list):
        raise ValueError(f"Invalid plugin dependency list: {location}")
    for plugin in plugins:
        if not isinstance(plugin, dict) or not isinstance(plugin.get("Name"), str):
            raise ValueError(f"Invalid plugin dependency: {location}")
        if plugin.get("Enabled") is not False and names_loader(plugin["Name"]):
            raise ValueError(f"Plugin descriptor depends on {plugin['Name']}: {location}")


def read_pak(repak, operation, pak, entry=None):
    command = [str(repak), operation, "--strip-prefix=", "--", str(pak)]
    if entry is not None:
        command.append(entry)
    location = str(pak) if entry is None else f"{pak}!{entry}"
    try:
        result = subprocess.run(command, capture_output=True, timeout=60)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ValueError(f"Cannot read PAK with repak: {location}: {error}") from error
    if result.returncode:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(f"Cannot read PAK with repak: {location}: {detail or result.returncode}")
    return result.stdout


def check_package(staged, repak):
    archives = []
    loose_count = 0
    for file in sorted(staged.rglob("*")):
        relative = file.relative_to(staged).as_posix()
        suffix = check_path(relative, relative)
        if not file.is_file():
            continue
        loose_count += 1
        if suffix == ".pak":
            archives.append(file)
        elif suffix == ".uplugin":
            check_descriptor(file.read_bytes(), relative)
    if not archives:
        raise ValueError(f"No .pak found in {staged}. Pass the staged mod folder.")

    entry_count = 0
    for pak in archives:
        listing = read_pak(repak, "list", pak)
        try:
            entries = listing.decode("utf-8").splitlines()
        except UnicodeDecodeError as error:
            raise ValueError(f"Cannot decode PAK file names in {pak}: {error}") from error
        for entry in filter(None, entries):
            entry_count += 1
            location = f"{pak.relative_to(staged)}!{entry}"
            if check_path(entry, location) == ".uplugin":
                check_descriptor(read_pak(repak, "get", pak, entry), location)
    return loose_count, len(archives), entry_count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("staged", type=Path, help="The staged folder for the mod.")
    parser.add_argument("--repak", type=Path, required=True, help="Path to the repak executable.")
    args = parser.parse_args()
    if not args.staged.is_dir():
        parser.error(f"Not a staged folder: {args.staged}")
    if not args.repak.is_file():
        parser.error(f"repak executable not found: {args.repak}")
    try:
        loose, archives, entries = check_package(args.staged.resolve(), args.repak.resolve())
    except (ValueError, OSError) as error:
        parser.exit(1, f"Package check failed: {error}\n")
    print(f"Checked {loose} loose files and {entries} entries in {archives} PAK(s).")
    print("No blocked file names or native loader declarations found in the checked files.")
    print("IoStore contents and runtime behavior were not checked.")


if __name__ == "__main__":
    main()
