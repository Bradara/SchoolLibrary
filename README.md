# SchoolLibrary

A web application for sharing learning resources (links to lessons, videos, documents, interactive pages) between teachers, students and parents. Resources are filed under subjects and grades (5th to 12th), carry a license, and can be shown right inside the page when the source allows embedding.

Live version: https://pmglibrary.duckdns.org/

Built with ASP.NET Core MVC (.NET 10), Entity Framework Core, PostgreSQL and ASP.NET Core Identity. The user interface is in Bulgarian; code, comments and documentation are in English.

## Contents

- [Concept](#concept)
- [Features](#features)
- [Roles and permissions](#roles-and-permissions)
- [Architecture](#architecture)
- [Data model](#data-model)
- [Services](#services)
- [Validation and security](#validation-and-security)
- [Seeding](#seeding)
- [Tests and coverage](#tests-and-coverage)
- [Getting started](#getting-started)
- [Deployment](#deployment)
- [Why PostgreSQL](#why-postgresql)

## Concept

Teachers keep good learning material scattered across bookmarks, chats and drives. SchoolLibrary puts it in one catalog:

- **Lessons** are materials a teacher adds and owns.
- **External resources** are freely licensed materials from the web (Wikisource, PhET, project Gutenberg, public videos), usually imported in bulk by an administrator from a CSV file.

Every resource has an author, a license (required) and any number of subjects and grades. Students and parents can browse, keep favorites, comment and report content. Administrators moderate.

## Features

- **Resource catalog** with search by title, filters (kind, subject, grade, owner, date), sorting and pagination.
- **Embedding**: YouTube (privacy-enhanced domain), Google Drive/Docs/Slides/Sheets, `.pdf`, `.html`, GitHub Pages and a short list of trusted domains (Wikisource, Wikibooks, Wikipedia, Gutenberg, Blockly Games, JSPaint). Everything else is shown as a link. Only `http` and `https` addresses are ever linked or embedded.
- **Licenses**: required for every resource, managed by administrators, shown in the list and the details.
- **Duplicate protection**: links are normalized and must be unique across the platform.
- **Favorites** with a personal paged list.
- **Comments** under a resource (up to 500 characters, newest first, paged).
- **Reports** for a resource or a comment (copyright, inappropriate content, broken link, other) with moderation in the Admin area.
- **Admin area**: dashboard, subjects, grades overview, licenses, users and roles, teacher approvals, reports, and CSV import of resources (preview with new/duplicate/invalid rows, then confirm).
- **Accounts**: registration as student, parent or teacher; account deletion (resources stay in the catalog, comments and favorites are removed).
- **Custom error pages** for 400, 403, 404 and 500.
- **Themes and layout**: responsive Bootstrap 5.3, compact side menu, four color themes saved in `localStorage`.

## Roles and permissions

Every registered user has the base role `User` plus one more role.

| Role | Can do |
|---|---|
| `Student`, `Parent` | Browse, favorites, comments, reports |
| `Teacher` | Everything above, plus create, edit and delete **own** resources |
| `Administrator` | Manage subjects, licenses, users, reports and imports; delete any resource (but not edit someone else's) |

Registration lets a user choose student, parent or teacher. Students and parents get the role at once. A teacher request waits for an administrator's approval and until then works as a student. The first registered user becomes the administrator.

## Architecture

The solution has four projects (`SchoolLibrary.slnx`):

| Project | Contents |
|---|---|
| `SchoolLibrary.Data` | Entities, enums, `ApplicationDbContext`, EF Core migrations, seeders |
| `SchoolLibrary.Services` | Service interfaces and implementations, `ServiceResult`, `PagedResult`, CSV import, DI registration (`AddApplicationServices`) |
| `SchoolLibrary.Web` | MVC controllers, `Areas/Admin`, Identity UI, Razor views, view models, middleware, static files |
| `SchoolLibrary.Tests` | xUnit tests with Moq and EF Core InMemory |

Dependencies point one way: `Web` → `Services` → `Data`. Controllers stay thin: they handle HTTP, validate input and redirect. All business rules live in services, which return a `ServiceResult` (success, or an error kind such as `NotFound`, `Conflict`, `Invalid`, `Forbidden` with a Bulgarian message).

Forms are bound to view models, not to entities, so a request cannot set fields the form does not have.

## Data model

| Entity | Notes |
|---|---|
| `ApplicationUser` | Identity user with first and last name, `CreatedOn`, teacher request flag |
| `Resource` | Title, description, URL (+ normalized URL, unique), kind, author, source, owner (nullable), license |
| `Category` | A subject. Many-to-many with `Resource` |
| `GradeLevel` | A grade, 5 to 12. Many-to-many with `Resource` |
| `License` | Name (unique), description, text URL. Restricted delete while resources use it |
| `Favorite` | Composite key (user, resource) |
| `Comment` | Text up to 500 characters, author, resource |
| `Report` | Reported resource or comment, reason, description, status, a copy of the reported text |

## Services

| Service | Responsibility |
|---|---|
| `IResourceService` | Search, filter, sort, paging; create, update, delete with ownership rules; duplicate check |
| `IResourceImportService` | Parse and validate the CSV (`;` separated, UTF-8), match subjects and grades by name, find duplicates, import |
| `ICategoryService` | Subjects CRUD, unique names, no delete while in use |
| `IGradeLevelService` | Grades list, resource counts |
| `ILicenseService` | Licenses CRUD, URL and name validation, no delete while in use |
| `IFavoriteService` | Add, remove, paged list |
| `ICommentService` | Add, delete (author or administrator), paged list |
| `IReportService` | Create (one open report per user and content), moderation, delete reported content |
| `IUserService` | Roles, teacher approvals, account deletion |
| `IStatisticsService` | Numbers for the admin dashboard |

## Validation and security

- **Client and server validation**: data annotations with Bulgarian messages, jQuery validation in the browser, and the same rules enforced again in services.
- **Database constraints**: required fields and maximum lengths, unique indexes (license name, normalized resource URL), composite keys, foreign keys with restricted delete where data must not disappear.
- **SQL injection**: only EF Core and parameterized queries, no raw SQL.
- **XSS**: Razor encodes all output, no `Html.Raw`; links and iframes accept only `http` and `https`; user content is shown with `white-space: pre-line`.
- **CSRF**: `AutoValidateAntiforgeryToken` is applied globally to every unsafe request.
- **Parameter tampering**: view models instead of entity binding, ownership and role checks in services, `[Authorize(Roles = ...)]` on controllers and on the whole Admin area.
- **Accounts**: the account locks for 5 minutes after 5 failed sign-ins.
- **Headers and cookies**: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and a Content-Security-Policy for framing, plugins, base URI and form targets; in Production every cookie is `Secure`; `AllowedHosts` limits the host names.
- **Size limits** on the CSV upload.

## Seeding

On start the app applies migrations and seeds reference data (only what is missing):

- grades 5 to 12,
- 31 subjects,
- 15 licenses (Creative Commons variants, public domain, MIT, Apache 2.0, GPL 2.0, standard YouTube license and others),
- roles, and the first administrator.

**Demo data** for development: accounts `admin`, `teacher`, `student` and `parent` at `@demo.local`, three lessons, comments, favorites and reports. It is added only in the `Development` environment and only when `Seed:DemoData` is `true`. The password comes from `Seed:DemoPassword`; set both with user secrets, they are not stored in the repository:

```
dotnet user-secrets set "Seed:DemoData" "true" --project SchoolLibrary.Web
dotnet user-secrets set "Seed:DemoPassword" "<a password>" --project SchoolLibrary.Web
```

## Tests and coverage

```
dotnet test SchoolLibrary.slnx
```

672 tests (xUnit, Moq, EF Core InMemory). They cover every service (success and failure paths), seeders, DI registration, controllers (redirects, messages, permissions), security conventions, error pages and helpers.

Line coverage measured with coverlet (`dotnet test -c Release --collect:"XPlat Code Coverage"`):

| Part | Coverage |
|---|---|
| `SchoolLibrary.Services` (the logic the assignment asks for) | **99.5%** |
| `SchoolLibrary.Data` without generated migrations | 97.9% |
| `SchoolLibrary.Web` without Razor views, Identity scaffolding and `Program.cs` | 99.1% |

The raw numbers per assembly are lower for Data and Web because generated migrations and compiled Razor views count as code; they are excluded above.

## Getting started

### Docker (recommended)

Requires Docker with Compose.

1. Copy `.env.example` to `.env` and set `POSTGRES_PASSWORD`.
2. Start the app with its own database:

   ```
   docker compose -f docker-compose.local.yml up --build
   ```

3. Open http://localhost:8081
4. Register. The first user becomes the administrator at the next start of the app, so restart the `web` container once after the first registration.

### dotnet run

Requires the .NET 10 SDK and a PostgreSQL server.

1. Set the connection string with user secrets (it is not stored in the repository):

   ```
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=schoollibrary;Username=...;Password=..." --project SchoolLibrary.Web
   ```

2. Run:

   ```
   dotnet run --project SchoolLibrary.Web
   ```

Migrations are applied automatically on start. To work with migrations by hand:

```
dotnet tool restore
dotnet ef migrations add <Name> --project SchoolLibrary.Data --startup-project SchoolLibrary.Web
dotnet ef database update --project SchoolLibrary.Data --startup-project SchoolLibrary.Web
```

### Configuration

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string (user secrets or environment) |
| `DataProtection__KeysPath` | Folder for cookie and antiforgery keys (a volume in Docker, so restarts keep users signed in) |
| `AllowedHosts` | Host names the app answers to, separated by `;` (default `*`) |
| `Security__AllowInsecureCookies` | `true` only for a local stack opened over plain HTTP |
| `Seed__DemoData`, `Seed__DemoPassword` | Demo data, Development only |

## Deployment

The image is built for `linux/arm64` and `linux/amd64` (multi-stage Dockerfile). The production stack runs the app on `127.0.0.1:8080`; a reverse proxy (nginx) terminates TLS and forwards `Host`, `X-Forwarded-For` and `X-Forwarded-Proto`. PostgreSQL runs as a separate container on a shared Docker network.

```
docker network create schoolnet
docker network connect schoolnet postgres
cp .env.example .env        # fill in the connection string and AllowedHosts, then chmod 600 .env
docker compose up -d --build
```

`scripts/backup-db.sh` dumps the database and removes old dumps; run it from cron.

## Why PostgreSQL

The application is deployed on an ARM64 (Ampere) server, which SQL Server does not support. PostgreSQL with Npgsql runs there natively, is free and works the same on a developer machine through Docker. There is a single database provider; tests use the EF Core InMemory provider.
