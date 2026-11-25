<p align="center">
  <img src="./logo.png" width="120" alt="QLauncher" title="QLauncher">
</p>

<h1 align="center">QLauncher</h1>

<p align="center">
  <b>Кроссплатформенный лаунчер для Minecraft на базе Avalonia UI (.NET 10)</b>
</p>

<p align="center">
  <i>Linux · Windows · macOS · Android (UI + интеграция с Amethyst / PojavLauncher)</i>
</p>

---

## Возможности

### Интерфейс и адаптация под окружение
* **Тёмная и светлая темы** + акцентные пресеты (`Sapphire`, `Amethyst`, `Emerald`, `Rose`, `Amber`) и тематические пресеты (`Nether`, `End`, `Sift`) со встроенными обоями.
* **Адаптивный TitleBar под любую ОС / DE / WM** (выбор в настройках или `Auto`):
  * **Windows 11** — классические кнопки справа.
  * **macOS** — кнопки-светофоры слева.
  * **GNOME (Adwaita)** и **KDE Plasma (Breeze)** — стилизованные кнопки управления окном.
  * **Tiling WM (`Niri`, `Hyprland`, `Sway`, `i3`, `bspwm`)** — компактный режим без лишних рамок, скруглений и кнопок сворачивания.
* **Векторные иконки, Toast-уведомления и диалоги** в едином стиле без системных окон.
* **Кроссплатформенное 3D-превью скина** (поддержка classic/slim, вращение мышью, зум, сохранение PNG).
* **Процедурные UI-звуки**, генерируемые на лету без сторонних аудиофайлов.

### Сборки, моды и каталоги
* **Прямая установка из Modrinth и CurseForge:** поиск и загрузка модов, ресурспаков, шейдеров, датапаков, плагинов и **готовых сборок (Modpacks)** прямо из каталога.
* **Импорт сборок:** поддержка **`.mrpack`** (Modrinth), **CurseForge `.zip`** (`manifest.json` + `overrides` + автозагрузка модов при наличии API-ключа), **Prism Launcher / MultiMC `.zip`** (`mmc-pack.json` + `instance.cfg`) и обычных `.zip` (включая Drag & Drop).
* **Создание сборок в один клик:** Vanilla, Fabric, Quilt (плюс Forge/NeoForge), быстрая установка Sodium/Iris.
* **Менеджер профиля:** включение/отключение и удаление модов, проверка обновлений модов, Zip-бэкапы и восстановление миров, создание ярлыков (`.lnk` на Windows, `.desktop` на Linux).

### Кроссплатформенность и стабильность
* **Автоподбор и загрузка OpenJDK** под текущую ОС и архитектуру (`windows`/`linux`/`mac`, `x64`/`aarch64`).
* **Нативные библиотеки (LWJGL) и правила запуска** подбираются под текущую ОС.
* **Зеркала загрузки (BMCLAPI / официальные)** с автоматической проверкой доступности.
* **Мониторинг серверов и Discord RPC:** онлайн и пинг серверов, учёт времени в сборке, статус в Discord со скрытием IP.

### Android (`QLauncher.Android`)
* Мобильный интерфейс на базе `Avalonia.Android` (`net10.0-android`) с общим `QLauncher.Core`, управлением аккаунтами, модами, сборками и настройками.
* Кнопка **«Открыть в Amethyst»** — запускает установленный на устройстве `Amethyst` / `PojavLauncher` / `MojoLauncher` (запуск самой JVM игры на Android требует нативного рантайма Amethyst).

---

## Сборка и запуск (Desktop)

### Требования
* **Linux**, **Windows 10 / 11** или **macOS**
* **.NET 10.0 SDK**

```bash
git clone https://github.com/seasharpic/qlauncher.git
cd qlauncher
dotnet build QLauncher.sln
dotnet run
```

### Публикация автономного бинарника
```bash
# Linux (x64)
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Windows (x64)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS (Apple Silicon / Intel)
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

---

## Сборка Android APK (`android-arm64`)

### Требования
* **.NET 10.0 SDK** + workload `android` (`sudo dotnet workload install android`)
* **JDK 21** и **Android SDK** (`platforms` + `build-tools`)

```bash
dotnet build QLauncher.Android/QLauncher.Android.csproj -c Release -r android-arm64 \
  -p:AndroidSdkDirectory="$HOME/Android/Sdk" \
  -p:JavaSdkDirectory="/usr/lib/jvm/java-21-openjdk"
```

Готовый подписанный APK будет в `QLauncher.Android/bin/Release/net10.0-android/android-arm64/com.qlauncher.android-Signed.apk`.

---

## Лицензия

Проект распространяется по свободной лицензии **GPL-3.0**.
