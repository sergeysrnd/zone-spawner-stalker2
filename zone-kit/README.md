# Zone Spawner для Zone Kit

Это разработка внутриигрового спаунера на штатных сценариях S.T.A.L.K.E.R. 2. Игровой пакет будет состоять из конфигов и ресурсов Zone Kit. Разработка не зависит от внешнего меню, UE4SS или DLL автозапуска.

Сейчас готовы данные и генерация конфига. Первый игровой прототип — собственный контейнер снабжения. Для него нужно создать ресурс в Zone Kit и проверить собранный пакет. [План разработки и критерии готовности](ROADMAP.md)

## Наборы для первого прототипа

| Идентификатор генератора | Содержимое |
| --- | --- |
| `ZoneSpawnerSupplyMed` | 1 аптечка |
| `ZoneSpawnerSupply545` | 30 патронов 5.45×39 ПС и 1 аптечка |
| `ZoneSpawnerSupply919` | 30 патронов 9×19 FMJ и 1 аптечка |

Наборы используют идентификаторы из [каталога Zone Spawner](../items.json). `Medkit`, `A545D` и `A919D` сверены с прототипами установленной сборки Steam `26123434`. В конфиге количество задано одинаковыми минимальным и максимальным значениями. Фактическое количество, доступ к контейнеру и поведение после загрузки сохранения нужно проверить в игре.

## Сборка конфига

Запусти генерацию конфига из корня репозитория с помощью Python 3.

```powershell
python zone-kit/build_supply.py
```

Скрипт прочитает [наборы](supply-kits.json) и запишет `build/zone-kit/Content/GameLite/GameData/ItemGeneratorPrototypes/ZoneSpawnerSupply.cfg`. Скрипт нужен только при разработке. Он проверяет идентификаторы, категории и количество и пишет файл в отдельную папку сборки.

## Подключение в редакторе

Для этого этапа нужен Zone Kit Phase 2 с компонентами **Zone Kit Editor** и **World Map**. [Официальный Zone Kit](https://store.epicgames.com/en-US/p/stalker-2-zone-kit) · [Руководство по установке и упаковке](https://zonekit-support.stalker2.com/en-US/kb/article/149/getting-started-with-zone-kit-or-zone-kit-full-guide)

1. Создай новый мод `ZoneSpawnerSupply` в Zone Kit Phase 2 и скопируй папку `Content` из папки сборки в папку этого мода. Перезагрузи конфиги и проверь, появились ли три генератора `ZoneSpawnerSupply…`.
2. В `WorldMap_WP` размести новый **Item Container Placeholder** на доступной площадке рядом с тестовым сохранением. Выбери прототип контейнера и включи `SpawnOnStart`. Zone Kit создаст для нового объекта конфиг в `Content/GameLite/ModGameData/SpawnActorPrototypes/WorldMap_WP/`. Назначь генератор `ZoneSpawnerSupplyMed` в настройках контейнера и проверь `ItemGeneratorSettings` в сгенерированном конфиге. [Руководство по объектам с SpawnOnStart](https://cdn.stalker2.com/guides/How_to_mod_placeholders.pdf)
3. В **Play In Editor** открой контейнер и проверь наличие ровно одной аптечки. Забери её, открой контейнер повторно и проверь сохранение и загрузку после получения предмета. Повтори проверку с `ZoneSpawnerSupply545`, где ожидаются 30 патронов и одна аптечка.
4. Собери мод кнопкой **Package Mod**. Для проверки Steam используй отдельное сохранение и действующую защиту Windows. GSC описывает закрытую подписку на тестовый мод через Workshop или mod.io. В редакторе можно проверить сценарий до загрузки на площадку. [Сборка и проверка игрового пакета](https://zonekit-support.stalker2.com/en-US/kb/article/149/getting-started-with-zone-kit-or-zone-kit-full-guide)

Перед проверкой в игре прогони `python zone-kit/check_package.py <папка-мода-в-SavedMods/Staged> --repak <путь-к-repak.exe>`. Скрипт проверяет отдельные файлы, содержимое списков PAK и объявления нативных модулей в доступных дескрипторах `.uplugin`. Содержимое IoStore и работу мода проверяем отдельно.

При выдаче по событию используем собственный Quest Blueprint с **Spawn** и **Set Item Generator**. Его можно запускать через **ModWorldSubsystem** при загрузке `WorldMap_WP`. Этот способ пригодится при переходе от готового контейнера к выдаче по выбору игрока. [Настройка Quest Blueprint](https://zonekit-support.stalker2.com/en-US/kb/article/165/zone-kit-quest-toolset-creating-your-first-quest-modification-from-a-clean-slate) · [Запуск через ModWorldSubsystem](https://cdn.stalker2.com/guides/Launching_new_quest_in_game_build_using_ModWorldSubsystem.pdf)

В первом прототипе интерфейсом служит открытие контейнера в игре. После проверки выдачи можно исследовать вызов своего интерфейса, поиск и ручной выбор количества. Для рюкзака, телепорта и горячей клавиши сначала нужно найти поддерживаемые точки входа Zone Kit.

[Формат конфигов](https://zonekit-support.stalker2.com/en-US/kb/article/145/config-patches) · [Устройство и упаковка модов Phase 2](https://zonekit-support.stalker2.com/en-US/kb/article/162/mod-game-features)
