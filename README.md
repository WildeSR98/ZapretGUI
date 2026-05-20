# 🛡 ZapretGUI

Графический менеджер для **zapret** (DPI bypass) на Windows с поддержкой **Telegram MTProto прокси**.

Основан на UI [FluxRoute](https://github.com/Flowseal/FluxRoute) с интеграцией логики из zapret-manager.

---

## 📸 Возможности

| Вкладка | Описание |
|---------|----------|
| 🏠 **Главная** | Запуск/остановка zapret, выбор стратегии, статус |
| 🔒 **TG Прокси** | Встроенный Telegram MTProto WS Bridge Proxy |
| 🔄 **Оркестратор** | Автоматический подбор лучшей стратегии |
| 🤖 **ИИ** | AI-подбор стратегий (эволюционный алгоритм) |
| ⬇️ **Обновление** | Авто-обновление engine и приложения |
| 🔍 **Диагностика** | Проверка подключений, компонентов |
| ⚙️ **Сервис** | Установка/удаление zapret как службы Windows |
| 📋 **Логи** | Подробные логи работы |

---

## 🚀 Быстрый старт

### Требования

- **Windows 10/11** (x64)
- [**.NET 8 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) — скачайте `Windows x64 Desktop Runtime`

### Установка

1. Скачайте **ZapretGUI-v1.0.0-portable.zip** из [Releases](https://github.com/WildeSR98/ZapretGUI/releases)
2. Распакуйте в любую папку
3. Запустите `ZapretGUI.exe`
4. На главной вкладке выберите стратегию и нажмите кнопку запуска

### Первый запуск

При первом запуске приложение автоматически:
- Скачает engine (winws.exe, стратегии, списки) от [Flowseal](https://github.com/Flowseal/zapret-discord-youtube)
- Предложит скачать компоненты TG Proxy (Python Embeddable + cryptography)

---

## 🔒 TG Прокси

Встроенный Telegram MTProto WebSocket Bridge прокси:

1. Перейдите на вкладку **TG Прокси**
2. При первом входе — согласитесь на установку компонентов
3. Нажмите **«Запустить прокси»**
4. Нажмите **«Открыть в Telegram»** — прокси автоматически настроится в клиенте

---

## ⚙️ Установка как служба Windows

На вкладке **Сервис**:
1. Выберите профиль на главной странице
2. Нажмите **«Установить службу»** — zapret будет работать в фоне
3. Служба запускается автоматически при старте Windows

> ⚠️ Требуются права администратора

---

## 🏗 Сборка из исходников

```bash
git clone https://github.com/WildeSR98/ZapretGUI.git
cd ZapretGUI
dotnet build ZapretGUI.sln
dotnet publish ZapretGUI/ZapretGUI.csproj -c Release -o publish-out --self-contained false
```

### Структура проекта

```
ZapretGUI/
├── ZapretGUI/           # WPF приложение (Views, ViewModels)
├── ZapretGUI.Core/      # Модели, сервисы, WinServiceManager (P/Invoke)
├── ZapretGUI.AI/        # AI-оркестратор (эволюционные стратегии)
├── ZapretGUI.Updater/   # Авто-обновления
├── engine/              # winws.exe, стратегии .bat, списки IP
├── tg-proxy/            # Telegram proxy (Python)
└── assets/              # Иконки
```

---

## 📦 Содержимое релиза

| Файл/Папка | Назначение |
|-----------|-----------|
| `ZapretGUI.exe` | Главное приложение |
| `*.bat` | Стратегии обхода DPI |
| `bin/` | winws.exe, WinDivert |
| `lists/` | Списки IP-адресов и доменов |
| `tg-proxy/` | Telegram MTProto прокси |
| `utils/` | Утилиты (targets, game filter) |

---

## 🙏 Благодарности

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — engine и стратегии
- [Flowseal/FluxRoute](https://github.com/Flowseal/FluxRoute) — UI/дизайн
- [Flowseal/tg-ws-proxy](https://github.com/Flowseal/tg-ws-proxy) — TG MTProto прокси

## 📄 Лицензия

MIT License
