# SaninaVPN Mobile - Сборка Android APK

## Требования

1. **.NET 10 SDK** с MAUI Android workload:
   ```bash
   dotnet workload install maui-android
   ```

2. **Visual Studio 2022+** (рекомендуется) с компонентом
   "Mobile development with .NET" (Xamarin/MAUI)

3. **Java JDK 17+** (Microsoft OpenJDK 17)

4. **Android SDK** (API 34+). Установите через
   `dotnet android` или Android Studio.

## Сборка

### 1. Восстановление
```bash
cd SaninaVPN.Mobile
dotnet restore
```

### 2. Debug APK
```bash
dotnet build -f net10.0-android -c Debug
```

### 3. Release APK (подписанный)
```bash
dotnet publish -f net10.0-android -c Release ^
  -p:AndroidSigningKeyStore=key.keystore ^
  -p:AndroidSigningKeyAlias=key ^
  -p:AndroidSigningKeyPass=password ^
  -p:AndroidSigningStorePass=password
```

APK будет в:
```
SaninaVPN.Mobile/bin/Release/net10.0-android/*-Signed.apk
```

## Установка на телефон

1. Включите "Неизвестные источники" в настройках Android
2. Скопируйте APK на телефон и откройте
3. При первом запуске дайте разрешение VPN
4. Добавьте подписку в разделе "Подписки"

## Структура проекта

```
SaninaVPN.Mobile/
├── Models/          # Модели данных (ServerInfo, enums)
├── Services/        # Бизнес-логика (парсер, конфиги, ping, подписки)
│   └── Android/     # PlatformHelper, SingBoxProcessManager
├── ViewModels/      # MVVM (MainViewModel с полной логикой)
├── Views/           # 4 MAUI страницы (XAML + code-behind)
├── Converters/      # StatusColor, ThemeToBool, PingToColor
├── Platforms/Android/# MainActivity, VpnService, Manifest
└── Resources/Raw/   # sing-box (58MB), geoip.dat, geosite.dat
```

## Важные файлы

| Файл | Назначение |
|------|-----------|
| `SaninaVpnService.cs` | Android VpnService — foreground service с VPN |
| `SingBoxProcessManager.cs` | Запуск/остановка sing-box процесса |
| `ConfigBuilder.cs` | Генерация sing-box конфига (6 протоколов) |
| `ServerParser.cs` | Парсинг vmess/vless/trojan/ss/hysteria2/tuic |
| `PlatformHelper.cs` | Распаковка бинарников из Assets |

## Примечание

Sing-box и базы GeoIP уже загружены в `Resources/Raw/`.
При первом запуске они распаковываются в `FilesDir` приложения.
