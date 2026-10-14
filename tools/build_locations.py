"""Derive factual hub landings and technician capabilities from the supported local game."""
import argparse, hashlib, json, pathlib, subprocess, tempfile
from read_cfg import read
from read_locres import read as read_locres

ROOT = pathlib.Path(__file__).resolve().parents[1]
NAMES = ['FastTravelLocationPrototypes', 'NPCPrototypes', 'ItemPrototypes', 'WeaponGeneralSetupPrototypes']
HUBS = [
    ('zalesie', 'Залесье', 'Малая Зона', ['Linza']),
    ('terricon', 'Террикон', 'Свалка', ['Konder']),
    ('asylum', 'Укрытие', 'Дикий остров', ['Hors']),
    ('sultansk', 'Султанск / Скадовск', 'Затон', ['kovyraska_0']),
    ('ikar', 'Икар', 'Затон', ['SerzEremeev']),
    ('shevchenko', 'Шевченко', 'Затон', []),
    ('rookievillage', 'Деревня новичков', 'Кордон', ['multik_0']),
    ('concreteplant', 'Цементный завод', 'Цементный завод', ['serzant_hmaruk_0']),
    ('rostok', 'Росток', 'Росток', ['Surup', 'Stepsel']),
    ('yanov', 'Станция Янов', 'Янов', ['garpia_0']),
    ('malahit', 'НТЦ «Малахит»', 'Малахит', ['semenyc_0']),
    ('himzavod', 'Химзавод', 'Химзавод', ['serzant_ivajlov_0']),
    ('pripyat', 'ДК «Энергетик»', 'Припять', ['medlak_0']),
    ('kazkovy', 'Пионерлагерь «Сказочный»', 'Выжженный лес', ['supack_technician_banzaj_0', 'supack_technician_banzaj_fourth_0']),
    ('sircaa', 'НИИЧАЗ', 'НИИЧАЗ', ['laborant_aupova']),
]
SPECIAL = {
    'Linza': 'Начальное оружие и лёгкая броня',
    'Konder': 'Оружие и броня среднего набора', 'Hors': 'Оружие и броня среднего набора',
    'Stepsel': 'Оружие и броня среднего набора', 'SerzEremeev': 'Оружие и броня среднего набора',
    'kovyraska_0': 'Оружие и броня среднего набора', 'multik_0': 'Оружие и броня среднего набора',
    'serzant_hmaruk_0': 'Широкий набор оружия и брони; часть экзоброни',
    'Surup': 'Широкий набор оружия и брони; часть экзоброни',
    'semenyc_0': 'Широкий набор оружия и брони; часть экзоброни',
    'serzant_ivajlov_0': 'Широкий набор оружия и брони; часть экзоброни',
    'garpia_0': 'Позднее оружие и экзоскелеты', 'medlak_0': 'Позднее оружие и экзоскелеты',
    'supack_technician_banzaj_0': 'Отдельные обвесы; до инструментов',
    'supack_technician_banzaj_fourth_0': 'Позднее оружие и экзоскелеты; после инструментов',
    'laborant_aupova': 'Ремонт; улучшения в прототипе не указаны',
}
NOTES = {
    'Stepsel': 'Сюжетный переезд: связан с Лесничеством и позднее с Ростоком. Его присутствие здесь зависит от этапа истории.',
    'supack_technician_banzaj_0': 'Начальный профиль. Полный набор появляется после сюжетных заданий и инструментов; текущий этап здесь не определяется.',
    'supack_technician_banzaj_fourth_0': 'Поздний профиль NPC из данных игры. Наличие этого профиля в каталоге не открывает его в сохранении.',
    'laborant_aupova': 'Сюжетный сервис НИИЧАЗ; доступность определяется прохождением.',
}

def derive(folder, strings, output):
    data = {name: read(folder / (name + '.cfg.bin')) for name in NAMES}
    catalog = json.loads((ROOT / 'items.json').read_text(encoding='utf8'))
    equipment = {}
    for item in catalog:
        if item['Restricted'] or item['Category'] not in ['Оружие', 'Броня', 'Шлемы и маски']: continue
        proto = data['ItemPrototypes'].get(item['Id'], {})
        setup = data['WeaponGeneralSetupPrototypes'].get(proto.get('GeneralWeaponSetup'), {}) if isinstance(proto.get('GeneralWeaponSetup'), str) else proto
        upgrades = {u for u in setup.get('UpgradePrototypeSIDs', {}).values() if isinstance(u, str) and u != 'empty' and u != 'FaustPsyResist_Quest_1_1'}
        if upgrades: equipment[item['Id']] = (item, upgrades)
    result = []
    for sid, name, region, technicians in HUBS:
        point = data['FastTravelLocationPrototypes'].get(sid, {}).get('PlayerLocation')
        landing = dict(Name=name, **{k: float(point[k]) for k in ['X', 'Y', 'Z']}) if point else None
        row = dict(Id=sid, Name=name, Region=region, Landing=landing, CoordinateSource='FastTravelLocationPrototypes.PlayerLocation' if landing else '',
                   Note='Статическая точка из проверенной сборки игры. Доступность базы и сервисов зависит от сюжета и репутации.', Technicians=[])
        if not landing: row['Note'] += ' В прототипах проводников нет готовой точки прибытия для этой базы.'
        if sid == 'kazkovy': row['Note'] += ' База из «Нерассказанных историй» появляется по ходу заданий.'
        for npc_sid in technicians:
            npc = data['NPCPrototypes'][npc_sid]
            npc_name = strings.get(npc.get('NameTextKey'))
            if not npc_name: raise ValueError('Missing technician name: ' + npc_sid)
            if npc_sid.startswith('supack_technician_banzaj_'): npc_name += ' (до инструментов)' if npc_sid.endswith('banzaj_0') else ' (поздний профиль)'
            allowed = {v['UpgradePrototypeSID'] for v in npc.get('Upgrades', {}).values() if isinstance(v, dict) and v.get('Enabled') == 'true'}
            fits = [dict(Id=i['Id'], Name=i['Name'], Category=i['Category'], Available=len(up & allowed), Total=len(up)) for i, up in equipment.values() if up & allowed]
            row['Technicians'].append(dict(Id=npc_sid, Name=npc_name, Specialization=SPECIAL[npc_sid], Note=NOTES.get(npc_sid, ''), Equipment=sorted(fits, key=lambda e: (e['Category'], e['Name']))))
        result.append(row)
    (output / 'locations.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    info = dict(steamBuild='25382007', count=len(result), landings=sum(x['Landing'] is not None for x in result), technicianProfiles=sum(len(x['Technicians']) for x in result),
                source='Local FastTravelLocationPrototypes.PlayerLocation, NPCPrototypes.Upgrades Enabled=true, ItemPrototypes/WeaponGeneralSetupPrototypes.UpgradePrototypeSIDs; names from Russian Game.locres',
                excludedUpgrades=['FaustPsyResist_Quest_1_1'], caveats='Static capability intersections, including attachment installation; no live quest/blueprint/reputation/NPC checks. Counts are not simultaneously installable branches. Landings not yet individually tested in game.',
                prototypeSHA256={n: hashlib.sha256((folder / (n + '.cfg.bin')).read_bytes()).hexdigest() for n in NAMES})
    (output / 'locations-info.json').write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    print('Locations:', info['count'], 'landings:', info['landings'], 'technician profiles:', info['technicianProfiles'])

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', type=pathlib.Path, required=True)
    parser.add_argument('--repak', type=pathlib.Path, required=True)
    parser.add_argument('--output-dir', type=pathlib.Path, default=ROOT)
    parser.add_argument('--data-dir', type=pathlib.Path, help='Developer cache of previously extracted CFG/ru-strings.json')
    args = parser.parse_args()
    info = json.loads((ROOT / 'catalog-info.json').read_text(encoding='utf8'))
    with (args.game_root / 'Stalker2/Binaries/Win64/Stalker2-Win64-Shipping.exe').open('rb') as file: digest = hashlib.file_digest(file, 'sha256').hexdigest()
    if digest.lower() != info['gameExeSHA256'].lower(): raise SystemExit('Unsupported game build')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    if args.data_dir:
        derive(args.data_dir, json.loads((args.data_dir / 'ru-strings.json').read_text(encoding='utf8')), args.output_dir)
    else:
        pak = args.game_root / 'Stalker2/Content/Paks/pakchunk0-Windows.pak'
        with tempfile.TemporaryDirectory(prefix='zone-spawner-locations-') as temp:
            folder = pathlib.Path(temp)
            for name in NAMES + ['Game']:
                source = 'Stalker2/Content/Localization/Game/ru/Game.locres' if name == 'Game' else 'Stalker2/Content/GameLite/GameData/' + name + '.cfg.bin'
                dest = folder / ('Game.locres' if name == 'Game' else name + '.cfg.bin')
                with dest.open('wb') as f: subprocess.run([str(args.repak.resolve()), 'get', str(pak), source], stdout=f, check=True)
            derive(folder, read_locres(folder / 'Game.locres'), args.output_dir)

if __name__ == '__main__': main()
