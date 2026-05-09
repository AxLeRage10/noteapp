# NoteApp — Консольная система заметок

Консольное приложение для ведения служебных заметок в ИТ-инфраструктуре VPN-сервиса.  
Разработано на C# (.NET 8), база данных — PostgreSQL 16, логирование — Serilog.

---

## Содержание

- [Требования](#требования)
- [Установка](#установка)
- [Настройка базы данных](#настройка-базы-данных)
- [Переменные окружения](#переменные-окружения)
- [Запуск](#запуск)
- [Команды](#команды)
- [Роли пользователей](#роли-пользователей)
- [Примеры использования](#примеры-использования)
- [Структура проекта](#структура-проекта)

---

## Требования

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 16](https://www.postgresql.org/download/windows/)
- Visual Studio 2022 (для разработки)

---

## Установка

1. Клонируй репозиторий:
```
git clone https://github.com/<логин>/noteapp.git
cd noteapp
```

2. Восстанови зависимости:
```
dotnet restore NoteApp/NoteApp.csproj
```

3. Собери проект:
```
dotnet build NoteApp/NoteApp.csproj
```

---

## Настройка базы данных

Открой pgAdmin или psql и выполни:

```sql
CREATE DATABASE noteapp;
CREATE USER noteapp_user WITH PASSWORD 'твой_пароль';
GRANT ALL PRIVILEGES ON DATABASE noteapp TO noteapp_user;
GRANT USAGE ON SCHEMA public TO noteapp_user;
GRANT CREATE ON SCHEMA public TO noteapp_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO noteapp_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO noteapp_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO noteapp_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO noteapp_user;
```

Таблицы создадутся автоматически при первом запуске приложения.

Создай первого администратора (замени хэш на свой — см. раздел ниже):
```sql
INSERT INTO users (username, password_hash, role, is_blocked, failed_attempts)
VALUES ('admin', '$2b$12$...', 'admin', false, 0);
```

Для генерации BCrypt-хэша пароля используй онлайн-сервис: https://bcrypt.online  
или команду в PowerShell после установки пакета BCrypt.Net-Next.

---

## Переменные окружения

Создай файл `.env` в папке `NoteApp/bin/Debug/net8.0-windows/` (только для разработки):

```
NOTEAPP_DB_HOST=localhost
NOTEAPP_DB_PORT=5432
NOTEAPP_DB_NAME=noteapp
NOTEAPP_DB_USER=noteapp_user
NOTEAPP_DB_PASSWORD=твой_пароль
NOTEAPP_UPDATE_URL=https://update.example.com
NOTEAPP_SESSION_SECRET=случайная-строка-минимум-32-символа
NOTEAPP_LOG_DIR=C:\NoteApp\logs
```

> ⚠️ Файл `.env` содержит секреты — никогда не добавляй его в Git.

---

## Запуск

### Интерактивный режим (рекомендуется)

Запусти без аргументов — появится запрос логина и пароля:
```
NoteApp.exe
```

После входа вводи команды в строку `[username]>`.

### CLI-режим (скриптование)

Передай команду напрямую как аргумент:
```
NoteApp.exe --login admin --password 12345678
NoteApp.exe --addNewNote "Перезапустить шлюз gateway-01"
NoteApp.exe --listNotes --limit 10
```

---

## Команды

### Авторизация
| Команда | Описание |
|---|---|
| `--login <user> --password <pwd>` | Войти в систему |
| `--logout` | Выйти из аккаунта |
| `--help` | Показать список команд |
| `exit` | Закрыть программу |

### Заметки
| Команда | Описание |
|---|---|
| `--addNewNote "<текст>"` | Добавить заметку (только кириллица, 1–500 символов) |
| `--listNotes` | Список своих заметок |
| `--listNotes --from 2025-01-01 --to 2025-12-31` | Фильтр по дате |
| `--listNotes --limit 20` | Ограничить количество |
| `--getNote --id <N>` | Просмотр заметки по ID |

### Управление пользователями *(только Администратор)*
| Команда | Описание |
|---|---|
| `--listUsers` | Список всех пользователей |
| `--addUser --username X --password Y --role Z` | Добавить пользователя |
| `--removeUser --username X` | Удалить пользователя |
| `--blockUser --username X` | Заблокировать бессрочно |
| `--blockUser --username X --until 2026-01-01` | Заблокировать до даты |

### Watchdog *(Администратор и Оператор)*
| Команда | Описание |
|---|---|
| `--watchdog --start` | Запустить мониторинг |
| `--watchdog --stop` | Остановить мониторинг |
| `--watchdog --status` | Текущие метрики CPU / RAM / HDD |
| `--watchdog --set-threshold --cpu 85 --ram 90 --hdd 80` | Настроить пороги (только Администратор) |

### Журнал событий *(Администратор и Оператор)*
| Команда | Описание |
|---|---|
| `--logs` | Последние 30 записей журнала |
| `--logs --type auth` | Только события авторизации |
| `--logs --type watchdog` | Только события мониторинга |
| `--logs --type db` | Только ошибки БД |
| `--logs --type user` | Только операции с пользователями |
| `--logs --limit 50` | Указать количество записей |

### Обновление
| Команда | Описание |
|---|---|
| `--update` | Проверить наличие обновлений |
| `--update --force` | Принудительно установить обновление *(только Администратор)* |

---

## Роли пользователей

| Роль | Значение | Доступ |
|---|---|---|
| `admin` | Администратор | Все функции системы |
| `operator` | Оператор watchdog | Заметки + watchdog + журнал watchdog |
| `user` | Пользователь | Только заметки |

---

## Примеры использования

**Войти и добавить заметку:**
```
NoteApp.exe

  Логин:  admin
  Пароль: ********

  ✓ Авторизация успешна. Добро пожаловать, admin [Администратор].

[admin]> --addNewNote "Перезапустить VPN-шлюз на узле gateway-01 после обновления сертификата"
  ✓ Заметка сохранена. ID: 1. Дата: 2026-05-09 16:30:00.
```

**Посмотреть список заметок:**
```
[admin]> --listNotes --limit 5

  ID     Дата                   Автор          Текст
  ───────────────────────────────────────────────────────────────────────────
  1      2026-05-09 16:30:00    admin          Перезапустить VPN-шлюз на узл…
```

**Добавить пользователя:**
```
[admin]> --addUser --username ivanov --password Pass123! --role user
  ✓ Пользователь 'ivanov' успешно создан. Роль: Пользователь.
```

**Статус watchdog:**
```
[admin]> --watchdog --status

  Watchdog активен. Интервал: 60 сек.
  CPU      43%        85%      [OK]
  RAM      61%        90%      [OK]
  HDD      55%        80%      [OK]
```

---

## Структура проекта

```
NoteApp/
├── NoteApp.csproj          # Конфигурация проекта
├── Program.cs              # Точка входа, CLI и интерактивный режим
├── version.json            # Текущая версия приложения
├── Core/
│   ├── Models.cs           # Модели данных
│   └── Validator.cs        # Валидация входных данных
├── Infrastructure/
│   ├── AppLogger.cs        # Логирование через Serilog
│   ├── Database.cs         # Подключение к PostgreSQL
│   ├── UserRepository.cs   # Репозиторий пользователей
│   └── NoteRepository.cs   # Репозиторий заметок
└── Services/
    ├── AuthService.cs          # Авторизация и сессии
    ├── NoteService.cs          # Работа с заметками
    ├── UserManagementService.cs # Управление пользователями
    ├── WatchdogService.cs      # Мониторинг нагрузки
    └── UpdateService.cs        # Обновление приложения

NoteApp.Tests/
├── NoteApp.Tests.csproj    # Тестовый проект (xUnit)
├── AuthServiceTests.cs     # Тесты авторизации
├── NoteTests.cs            # Тесты заметок
└── DatabaseAndUpdateTests.cs # Тесты БД и обновлений
```

---

## Лицензия

Учебный проект. Курсовая работа по дисциплине «Тестирование программного обеспечения», 2026.
