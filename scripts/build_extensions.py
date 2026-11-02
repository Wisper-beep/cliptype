"""Build deterministic, unsigned Chromium/Firefox development archives (stdlib only)."""
import json
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "extension"
DIST = ROOT / "dist"

def manifest_for(browser):
    manifest = json.loads((SOURCE / "manifest.json").read_text(encoding="utf-8"))
    if browser == "firefox":
        manifest.pop("minimum_chrome_version", None)
        manifest["background"] = {"scripts": ["background.js"]}
        for command in manifest["commands"].values():
            command["suggested_key"] = {"default": command["suggested_key"]}
        manifest["browser_specific_settings"] = {"gecko": {
            "id": "cliptype@wisper-beep.github.io", "strict_min_version": "142.0",
            "data_collection_permissions": {"required": ["none"]}}}
    return manifest

def build():
    DIST.mkdir(exist_ok=True)
    for browser, title in (("chromium", "Chromium"), ("firefox", "Firefox")):
        destination = DIST / browser
        destination.mkdir(exist_ok=True)
        contents = {}
        for file in sorted(SOURCE.rglob("*")):
            if file.is_file() and not file.is_symlink():
                contents[file.relative_to(SOURCE).as_posix()] = file.read_bytes()
        contents["manifest.json"] = (json.dumps(manifest_for(browser), ensure_ascii=False, indent=2) + "\n").encode()
        contents["LICENSE"] = (ROOT / "LICENSE").read_bytes()
        with ZipFile(DIST / f"ClipType-{title}.zip", "w", compression=ZIP_DEFLATED) as archive:
            for name, data in sorted(contents.items()):
                info = ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
                info.compress_type = ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                archive.writestr(info, data)
                target = destination / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
        print(f"Built ClipType-{title}.zip (unsigned development build)")
    (DIST / "ClipType-Extension.zip").write_bytes((DIST / "ClipType-Chromium.zip").read_bytes())

if __name__ == "__main__":
    build()
