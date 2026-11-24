<div align="center">

<img src="assets/hivox-logo.svg" alt="Hivox VPN Logo" width="112" />

# Hivox VPN

**Современный кроссплатформенный VPN-клиент для Windows и Android**  
Встроенные ядра **Xray-core** и **sing-box**, режимы **Proxy** и **TUN**, интерфейс в стиле **Material 3**.

[![Latest Release](https://img.shields.io/github/v/release/GagaNojka228/Hivox?style=for-the-badge&color=6750A4&label=Release)](https://github.com/GagaNojka228/Hivox/releases/latest)
[![Total Downloads](https://img.shields.io/github/downloads/GagaNojka228/Hivox/total?style=for-the-badge&color=006A6A&label=Downloads)](https://github.com/GagaNojka228/Hivox/releases)
[![Windows x64](https://img.shields.io/badge/Windows-10%20%2F%2011%20(x64)-0078D4?style=for-the-badge&logo=windows&logoColor=white)](#скачать-v210)
[![Android](https://img.shields.io/badge/Android-APK-3DDC84?style=for-the-badge&logo=android&logoColor=black)](#скачать-v210)

[Скачать](#-скачать-v210) · [Возможности](#-возможности) · [Протоколы](#-поддерживаемые-протоколы) · [Быстрый старт](#-быстрый-старт) · [SHA-256](#-проверка-целостности-sha-256)

</div>

---

## 📦 Скачать (v2.1.0)

Все актуальные сборки доступны на странице [Releases](https://github.com/GagaNojka228/Hivox/releases/latest).

| Платформа | Файл | Описание | Размер | Скачать |
| :--- | :--- | :--- | :---: | :---: |
| **Windows 10/11 (x64)** | `Hivox-Setup-v2.1.0-win-x64.exe` | Установщик (всё включено) | ~148.4 MB | [Скачать EXE](https://github.com/GagaNojka228/Hivox/releases/download/v2.1.0/Hivox-Setup-v2.1.0-win-x64.exe) |
| **Windows 10/11 (x64)** | `Hivox-Portable-v2.1.0-win-x64.zip` | Портативная версия (без установки) | ~89.5 MB | [Скачать ZIP](https://github.com/GagaNojka228/Hivox/releases/download/v2.1.0/Hivox-Portable-v2.1.0-win-x64.zip) |
| **Android** | `Hivox-v2.1.0-android.apk` | Установочный пакет для смартфона | ~65.6 MB | [Скачать APK](https://github.com/GagaNojka228/Hivox/releases/download/v2.1.0/Hivox-v2.1.0-android.apk) |
| **Checksums** | `SHA256SUMS.txt` | Контрольные суммы файлов | 288 B | [Скачать TXT](https://github.com/GagaNojka228/Hivox/releases/download/v2.1.0/SHA256SUMS.txt) |

---

## ✨ Возможности

- **Два встроенных ядра:** `Xray-core` и `sing-box` уже упакованы в сборку — ничего дополнительно скачивать не нужно.
- **Режимы работы:** системный **Proxy** (HTTP / SOCKS5) и полноценный **TUN**-режим для всего трафика.
- **Подписки и импорт:** добавление подписок по URL, автообновление по расписанию, массовый импорт ссылок.
- **Умный список серверов:** поиск в реальном времени, сортировка по пингу и имени, проверка доступности (TCP/ICMP), отображение страны (GeoIP).
- **Встроенная диагностика:** тест утечки DNS (*DNS Leak Test*) и замер скорости (*Speed Test*) прямо через активный прокси.
- **Интерфейс Material 3:** тёмная и светлая темы, график скорости в реальном времени, системный трей и история подключений.

---

## 🛡️ Поддерживаемые протоколы

| Протокол | Поддержка | Ядро |
| :--- | :---: | :--- |
| **VLESS** (Reality / Vision / gRPC / WS) | ✅ | Xray-core / sing-box |
| **VMess** | ✅ | Xray-core / sing-box |
| **Trojan** | ✅ | Xray-core / sing-box |
| **Shadowsocks** | ✅ | Xray-core / sing-box |
| **Hysteria2** | ✅ | sing-box |
| **TUIC** | ✅ | sing-box |

---

## 🚀 Быстрый старт

### Windows (x64)
1. Скачайте **`Hivox-Setup-v2.1.0-win-x64.exe`** и запустите установку (либо распакуйте **`Hivox-Portable-v2.1.0-win-x64.zip`** в любую папку и запустите `Hivox.exe`).
2. Нажмите **`+`**, чтобы добавить ссылку на подписку или отдельный сервер.
3. Выберите сервер из списка и нажмите кнопку подключения.

### Android
1. Скачайте **`Hivox-v2.1.0-android.apk`** на телефон и разрешите установку из браузера / файлового менеджера.
2. Откройте **Hivox**, добавьте подписку или конфигурацию.
3. Нажмите кнопку подключения и подтвердите создание VPN-профиля в системе Android.

---

## 🔐 Проверка целостности (SHA-256)

<details>
<summary>Показать контрольные суммы SHA-256 и отпечаток сертификата Android</summary>

```text
000d1ebd4aea33eb1ea93ce0b7869b01283306b521b609a71f93fcd46a8d5c4e  Hivox-Portable-v2.1.0-win-x64.zip
9bde8960899982456155de157895f2e5cd23c33f117f2968d1e5f8a66332a943  Hivox-Setup-v2.1.0-win-x64.exe
c1b61cd3862f5f472f273914d665b444f8f80fe91c5df4b7363ca232965c9276  Hivox-v2.1.0-android.apk
```

**Проверка в Windows PowerShell:**
```powershell
Get-FileHash .\Hivox-Setup-v2.1.0-win-x64.exe -Algorithm SHA256
```

**Сертификат подписи Android APK (SHA-256):**
```text
4a67c92f808e7ad89b241dbc0e10732aa6dd9e81fcbd6430dcbd0939812a9f71
```

</details>