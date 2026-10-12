# SchoolLibrary

Курсов проект по **ASP.NET Fundamentals** (SoftUni, октомври 2026).

Уеб приложение за споделяне на учебни ресурси между учителите в едно училище.
Всеки учител може да добавя връзки към полезни материали (Google Drive, YouTube,
 OneDrive), организирани по предмет и клас. 

## Технологии

- ASP.NET Core MVC (.NET 10)
- Entity Framework Core + SQL Server LocalDB
- ASP.NET Core Identity (email + парола)
- Bootstrap 5

## Стартиране

Изисквания:
- Visual Studio 2022+ с .NET 8+ SDK
- SQL Server LocalDB (идва с VS installer)

1. Клонирай repo-то
2. Отвори `SchoolLibrary.sln` във Visual Studio
3. F5 за стартиране
4. Регистрирай първи account — автоматично получаваш Admin права.

## Развитие — статус

### Етап 1 — Setup ✅
- [x] Създаване на MVC проект с Individual Auth
- [x] Преминаване от SQLite към SQL Server LocalDB
- [x] Персонализация на Home и About страници

### Етап 2 — Модел и CRUD ✅
- [x] Entity: Resource, Category, GradeLevel
- [x] Пълен CRUD за Resource
- [x] Списък с филтри

### Етап 3 — Роли и валидация ✅
- [x] Admin роля (bootstrap на първия регистриран)
- [x] CategoriesController с Admin-only CRUD
- [x] Ограничения по authorization
- [x] Client + server side validation

### Етап 4 — Полиране ⏳
- [x] Bootstrap layout полиране (responsive design)
- [x] Error/404 страници
- [ ] Финален README

## Реализирани функционалности
- [x] Публикувано на Azure F1 + Azure SQL Free Tier 
      https://schoollibrary-pmg-edftb9dedaehbna3.westus2-01.azurewebsites.net/ 
	  (моля изчакайте да се събуди от auto-pause на F1)
- [x] Вграждане в iframe за споделени ресурси в Google drive, статични страници, 
	  Github pages и свободни .pdf файлове от мрежата

## Идеи за бъдещо развитие (може би в следващ курс)

- [ ] Google OAuth login
- [ ] Добавянена pagination
- [ ] File upload за EPUB и изображения (PDF, DOCX)
- [ ] Markdown ресурси със server-side рендеринг
- [ ] Диаграми (Mermaid) и формули (KaTeX) в Markdown
- [ ] Различни видове ресурси, като тестове, интерактивни упражнения и уроци, H5P, SCORM пакети
- [ ] Поддръжка на различни роли (учител, ученик, родител)
- [ ] Docker deploy на Oracle OCI - Free Tier(Ampere ARM) или Azure Free Tier
- [ ] AI асистент за въпроси към ресурси и помощ за учениците за усвояване на нови знания
