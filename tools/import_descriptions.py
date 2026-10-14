"""Import game descriptions locally; never distribute the generated text."""
import argparse, hashlib, json, pathlib, subprocess, tempfile
from read_locres import read

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-root', type=pathlib.Path, required=True)
parser.add_argument('--repak', type=pathlib.Path, required=True)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
info = json.loads((root/'catalog-info.json').read_text(encoding='utf-8-sig'))
exe = args.game_root/'Stalker2/Binaries/Win64/Stalker2-Win64-Shipping.exe'
with exe.open('rb') as file:
    digest = hashlib.file_digest(file, 'sha256').hexdigest()
if digest.lower() != info['gameExeSHA256'].lower():
    raise SystemExit('Unsupported game build. Refresh and validate the catalog before importing.')
pak = args.game_root/'Stalker2/Content/Paks/pakchunk0-Windows.pak'
result = subprocess.run([str(args.repak.resolve()), 'get', str(pak),
    'Stalker2/Content/Localization/Game/ru/Game.locres'], check=True, stdout=subprocess.PIPE)
with tempfile.TemporaryDirectory(prefix='zone-spawner-locres-') as folder:
    locres = pathlib.Path(folder)/'Game.locres'; locres.write_bytes(result.stdout)
    strings = read(locres)
items = json.loads((root/'items.json').read_text(encoding='utf8'))
count = 0
for item in items:
    text = strings.get('sid_items_'+item['Id']+'_description')
    if text:
        item['Description'] = text; count += 1
(root/'items.json').write_text(json.dumps(items, ensure_ascii=False, indent=2), encoding='utf8')
print('Imported descriptions locally:', count)
