# SchoolLibrary

Курсов проект по **ASP.NET Fundamentals** (SoftUni, октомври 2026).

Уеб приложение за споделяне на учебни ресурси между учителите в едно училище.
Всеки учител може да добавя връзки към полезни материали (Google Drive, YouTube,
 OneDrive), организирани по предмет и клас. 

## Технологии

- ASP.NET Core MVC (.NET 8)
- Entity Framework Core + SQL Server LocalDB
- ASP.NET Core Identity (email + парола)
- Bootstrap 5

## Стартиране

Изисквания:
- Visual Studio 2022+ с .NET 8 SDK
- SQL Server LocalDB (идва с VS installer)

1. Клонирай repo-то
2. Отвори `SchoolLibrary.sln` във Visual Studio
3. Стартирай в Package Manager Console → `Add-Migration InitialCreate`
4. Приложи migrations: в Package Manager Console → `Update-Database`
5. F5 за стартиране

## Развитие — статус

### Етап 1 — Setup ✅
- [x] Създаване на MVC проект с Individual Auth
- [x] Преминаване от SQLite към SQL Server LocalDB
- [x] Персонализация на Home и About страници

### Етап 2 — Модел и CRUD ⏳
- [x] Entity: Resource, Category, GradeLevel
- [ ] Пълен CRUD за Resource
- [ ] Списък с pagination и филтри

### Етап 3 — Роли и валидация ⏳
- [ ] Admin роля (bootstrap на първия регистриран)
- [ ] Ограничения по authorization
- [ ] Client + server side validation

### Етап 4 — Полиране ⏳
- [ ] Bootstrap layout полиране (responsive design)
- [ ] Error/404 страници
- [ ] Финален README

## Идеи за бъдещо развитие (може би в следващ курс)

- [ ] Google OAuth login
- [ ] File upload за EPUB и изображения (PDF, DOCX)
- [ ] Markdown ресурси със server-side рендеринг
- [ ] Диаграми (Mermaid) и формули (KaTeX) в Markdown
- [ ] Docker deploy на Oracle OCI - Free Tier(Ampere ARM) или Azure Free Tier
- [ ] AI асистент за въпроси към ресурси и помощ за учениците за усвояване на нови знания
