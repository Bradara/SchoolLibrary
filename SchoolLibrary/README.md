# SchoolLibrary

Курсов проект по **ASP.NET Advanced** (SoftUni, октомври 2026).

Уеб приложение за споделяне на учебни ресурси между учителите в едно училище.
Всеки учител може да добавя връзки към полезни материали (Google Drive, YouTube,
OneDrive), организирани по предмет и клас. Един ресурс може да е към няколко
предмета и няколко класа.

Работеща версия: https://pmglibrary.duckdns.org/

## Технологии

- ASP.NET Core MVC (.NET 10)
- Entity Framework Core + PostgreSQL (Npgsql)
- ASP.NET Core Identity (email + парола)
- Bootstrap 5
- Docker / Docker Compose (образ за linux/arm64 и linux/amd64)

## Стартиране

### Вариант 1 — Docker (препоръчително)

Изисквания: Docker с Compose.

1. Клонирай repo-то.
2. Копирай `.env.example` като `.env` и задай `POSTGRES_PASSWORD`.
3. Стартирай приложението заедно с локална база:

   ```
   docker compose -f docker-compose.local.yml up --build
   ```

4. Отвори http://localhost:8081
5. Регистрирай първия потребител. Първият регистриран получава ролята Admin
   при следващото стартиране на приложението (рестартирай контейнера `web`
   след първата регистрация).

### Вариант 2 — dotnet run

Изисквания: .NET 10 SDK и PostgreSQL.

1. Задай connection string с user secrets (стойността не се записва в repo-то):

   ```
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=schoollibrary;Username=...;Password=..." --project SchoolLibrary
   ```

2. Стартирай: `dotnet run --project SchoolLibrary`. Миграциите се прилагат
   автоматично при старт и се добавят начални данни (класове 5–12, предмети).

## Деплой (Docker + nginx)

Приложението е контейнер, който слуша на `127.0.0.1:8080`. TLS се прави от
reverse proxy (nginx), а PostgreSQL е отделен контейнер в обща Docker мрежа.

1. Създай мрежата и закачи базата към нея:

   ```
   docker network create schoolnet
   docker network connect schoolnet postgres
   ```

2. Копирай `.env.example` като `.env` (`chmod 600 .env`) и задай:

   ```
   ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=schoollibrary;Username=...;Password=...
   ```

3. Стартирай: `docker compose up -d --build`
4. В nginx: `proxy_pass http://127.0.0.1:8080;` със заглавия `Host`,
   `X-Forwarded-For` и `X-Forwarded-Proto`.
5. Архивиране на базата: `scripts/backup-db.sh` (пуска се от cron).

## Развитие — статус

### Етап 1 — Setup ✅
- [x] Създаване на MVC проект с Individual Auth
- [x] Преминаване към PostgreSQL
- [x] Персонализация на Home и About страници

### Етап 2 — Модел и CRUD ✅
- [x] Entity: Resource, Category (предмет), GradeLevel (клас)
- [x] Пълен CRUD за Resource, много предмети и класове към ресурс
- [x] Списък с филтри и сортиране

### Етап 3 — Роли и валидация ✅
- [x] Admin роля
- [x] Управление на предметите само от Admin
- [x] Ограничения по authorization
- [x] Client + server side validation

### Етап 4 — Полиране ⏳
- [x] Bootstrap layout полиране (responsive design)
- [x] Error/404 страници
- [x] Docker пакетиране и деплой
- [ ] Вертикално меню и цветови теми

## Реализирани функционалности
- [x] Вграждане в iframe за споделени ресурси в Google drive, статични страници,
      Github pages и свободни .pdf файлове от мрежата
- [x] Много предмети и класове за един ресурс
- [x] Филтриране и сортиране на ресурсите
- [x] Docker образ и деплой на Oracle OCI Free Tier (Ampere ARM)

## Идеи за бъдещо развитие (може би в следващ курс)

- [ ] Google OAuth login
- [ ] Добавяне на pagination
- [ ] File upload за EPUB и изображения (PDF, DOCX)
- [ ] Markdown ресурси със server-side рендеринг
- [ ] Диаграми (Mermaid) и формули (KaTeX) в Markdown
- [ ] Различни видове ресурси, като тестове, интерактивни упражнения и уроци, H5P, SCORM пакети
- [ ] Поддръжка на различни роли (учител, ученик, родител)
- [ ] AI асистент за въпроси към ресурси и помощ за учениците за усвояване на нови знания
