# Zone Spawner для Zone Kit

Это разработка внутриигрового спаунера на штатных сценариях S.T.A.L.K.E.R. 2. Игровой пакет будет состоять из конфигов и ресурсов Zone Kit. Разработка не зависит от внешнего меню, UE4SS или DLL автозапуска.

Сейчас готовы данные и генерация конфига. Для первого игрового прототипа нужно собрать оригинальный сценарий в Zone Kit, подключить конфиг и проверить упакованный мод. На этом этапе нет мода для установки в Steam и нет внутриигрового меню.

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

1. Создай новый мод `ZoneSpawnerSupply` в Zone Kit Phase 2 и скопируй папку `Content` из папки сборки в папку этого мода. Перезагрузи конфиги и проверь, видит ли редактор три `ZoneSpawnerSupply…` в поле **Item Generator**.
2. Для первого прогона создай свой Quest Blueprint и место снабжения с одним **Item Container Placeholder** в доступной точке карты. В сценарии добавь **Spawn** и **Set Item Generator** для этого контейнера. В **Set Item Generator** выбери `ZoneSpawnerSupplyMed` и укажи свой контейнер вместо цели `Target Player`. Для патронов переключи генератор либо добавь отдельные точки с их наборами. [Руководство по Quest Blueprint](https://zonekit-support.stalker2.com/en-US/kb/article/165/zone-kit-quest-toolset-creating-your-first-quest-modification-from-a-clean-slate)
3. В **Play In Editor** проверь, что открывается именно новый контейнер, он выдаёт заданное количество и повторное открытие не добавляет вещи. Проверь загрузку сохранения до и после получения лута. После успешной проверки запусти свой сценарий через **ModWorldSubsystem** при загрузке карты и собери мод кнопкой **Package Mod**. [Запуск своего сценария из ModWorldSubsystem](https://cdn.stalker2.com/guides/Launching_new_quest_in_game_build_using_ModWorldSubsystem.pdf)
4. Проверь упакованный мод на отдельном сохранении с включённой защитой Windows. До публикации нужно подтвердить, что запуск, получение предметов и загрузка работают без UE4SS, а в пакете только ресурсы мода.

В первом прототипе интерфейсом служит открытие контейнера в игре. После проверки выдачи можно исследовать вызов своего интерфейса, поиск и ручной выбор количества. Для рюкзака, телепорта и горячей клавиши сначала нужно найти поддерживаемые точки входа Zone Kit.

[Формат конфигов](https://zonekit-support.stalker2.com/en-US/kb/article/145/config-patches) · [Устройство и упаковка модов Phase 2](https://zonekit-support.stalker2.com/en-US/kb/article/162/mod-game-features)
