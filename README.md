# SchoolLibrary

A web application for sharing learning resources between teachers, students and parents: links to lessons, videos and interactive pages, or files (PDF, EPUB, Markdown) uploaded to the platform. Resources are filed under subjects and grades (5th to 12th), carry a license, and can be shown right inside the page — embedded when the source allows it, or previewed in the browser when the file is uploaded.

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
- **Uploaded materials** are files a teacher uploads to the platform (PDF, EPUB or Markdown), previewed in the browser.

Every resource has an author, a license (required) and any number of subjects and grades. Students and parents can browse, keep favorites, comment and report content. Administrators moderate.

Teachers also build **quizzes** from their own **question bank**: open practice tests students can take any time, and timed exams for a class that students enter with a code, without an account. Questions can be imported from and exported to Moodle, so a teacher can move them between the two.

## Features

- **Resource catalog** with search by title, filters (kind, subject, grade, owner, date), sorting and pagination.
- **Embedding**: YouTube (privacy-enhanced domain), Google Drive/Docs/Slides/Sheets, `.pdf`, `.html`, GitHub Pages and a short list of trusted domains (Wikisource, Wikibooks, Wikipedia, Gutenberg, Blockly Games, JSPaint). Everything else is shown as a link. Only `http` and `https` addresses are ever linked or embedded.
- **Uploaded files** (PDF, EPUB, Markdown, up to 25 MB) with an in-browser preview: PDF drawn with pdf.js (the same on phones, with zoom, page navigation, selectable text, dark pages, the page kept for the next visit and read aloud), Markdown rendered to sanitized HTML (Markdig + HtmlSanitizer), EPUB read with epub.js. The file type is verified from the content, not from the name. Files are stored outside the database behind a storage abstraction; the same file cannot be uploaded twice (checked by hash).
- **Find resources** (teachers): a topic is searched in a fixed list of sources (Wikipedia, Wikisource, Wikibooks, PhET, Project Gutenberg, Internet Archive, GeoGebra, Scratch and, when an API key is set, YouTube). Each suggestion shows its source, license, whether it can be embedded and whether the catalog already has it; nothing is saved until the teacher ticks it. Users never give an address to fetch, so the server cannot be made to call arbitrary hosts. YouTube results are embeddable videos only, with strict safe search; Creative Commons videos get CC BY 3.0, the rest the standard YouTube license.
- **Licenses**: required for every resource, managed by administrators, shown in the list and the details.
- **Duplicate protection**: links are normalized and must be unique across the platform.
- **Favorites** with a personal paged list.
- **Comments** under a resource (up to 500 characters, newest first, paged).
- **Online libraries**: a page of cards linking to external digital libraries, managed by administrators. Each site's icon is downloaded once by the server and served from the platform, so visitors' browsers never contact the other site.
- **Question bank**: teachers keep questions in a tree of sections (topics and subtopics), move questions between sections and reuse them in any number of quizzes.
- **Seven question types**:

  | Type | The student | Grading |
  |---|---|---|
  | Single choice | picks one option | the correct option earns all points |
  | True/false | picks "True" or "False" | as single choice |
  | Multiple choice | ticks any options | each option has a share; the sum of the ticked shares, cut to 0–100% (ticking everything earns nothing) |
  | Short answer | types a word or phrase | the best matching accepted answer (`*` is a wildcard, optional case sensitivity, Latin look-alike letters are read as Cyrillic) |
  | Numerical | types a number (`,` or `.`) | correct within a tolerance (±) |
  | Matching | picks an answer for each item | the share of correct pairs; extra answers can be added as distractors |
  | Description | — | text between the questions (instructions, a passage to read); no points, no number |

  Every question has its own points (1–100). The score is the sum of the earned points, partial credit included, and the grade uses the Bulgarian 2–6 scale (3 from 30%, 4 from 50%, 5 from 70%, 6 from 85%).
- **Quizzes**: a quiz is put together from pinned bank questions and "draw N random questions from a section" items, with optional shuffling of questions and options. *Practice* quizzes are open to everyone, with instant feedback and unlimited retries, and can be linked to a lesson.
- **Class exams**: a teacher keeps a list of classes and assigns a practice quiz to a class as an exam with its own code, time limit and proctoring limit. Students enter the code, then their name and class number (no account needed); each class number gets one attempt. The teacher opens and closes the exam and decides when the results are released; after that a student sees the points, the grade and the answers. Light proctoring: a fullscreen button, counting how many times the student leaves the tab or window, and auto-submit once the limit is passed. Teachers review the attempts (score, time, leave count) with pagination.
- **Question import and export**: questions are imported into a section from Aiken text or Moodle XML (every type above, with points and answer shares; unsupported types are skipped with a reason). A section (with its subsections as Moodle categories) or a quiz can be downloaded as Moodle XML and imported into Moodle or back into the platform.
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
| `Teacher` | Everything above, plus create, edit and delete **own** resources; find resources; own question bank, quizzes, classes and exams |
| `Administrator` | Manage subjects, licenses, online libraries, users, reports and imports; delete any resource (but not edit someone else's); see and manage every teacher's quizzes and question bank |

Registration lets a user choose student, parent or teacher. Students and parents get the role at once. A teacher request waits for an administrator's approval and until then works as a student. The first registered user becomes the administrator.

Practice quizzes are open to any visitor; exams are solved anonymously by code, so a student needs no account to sit one.

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
| `Resource` | Title, description, URL (+ normalized URL, unique), kind (lesson, external or upload), author, source, owner (nullable), license |
| `ResourceFile` | An uploaded file's metadata: storage key (unique), original name, content type, size, SHA-256; one per resource. The bytes live in the file storage, not in the database |
| `Category` | A subject. Many-to-many with `Resource` |
| `GradeLevel` | A grade, 5 to 12. Many-to-many with `Resource` |
| `License` | Name (unique), description, text URL. Restricted delete while resources use it |
| `Favorite` | Composite key (user, resource) |
| `Comment` | Text up to 500 characters, author, resource |
| `Report` | Reported resource or comment, reason, description, status, a copy of the reported text |
| `OnlineLibrary` | An external library card: name, description, URL, icon, sort order, and the key of its downloaded site icon |
| `Section` | A topic in a teacher's question bank; sections form a tree (parent section, nullable) |
| `Question` | Text, type (one of the seven), points, case sensitivity for short answers, display order; belongs to a bank section (or, for older quizzes, directly to a quiz) |
| `AnswerOption` | Option text, share of the points in percent (−100 to 100), tolerance (numerical), matching answer (matching), display order |
| `Quiz` | Title, description, mode, shuffle settings, published flag, optional lesson, owner (nullable) |
| `QuizItem` | One line of a quiz: a pinned bank question, or "draw N random questions from a section" |
| `SchoolClass` | A teacher's class (a name, unique per teacher) |
| `ExamAssignment` | A quiz assigned to a class as an exam: code (unique), time limit, proctoring limit, open flag, results released flag |
| `QuizAttempt` | One run of a quiz: unguessable token (unique), student name and class number (unique per class exam), start and submit times, score and maximum (decimal, for partial credit), leave count and flag |
| `AttemptQuestion` | The questions served to an attempt, in their order (a snapshot taken at the start, so random draws stay fixed) |
| `AttemptAnswer` | A student's answer to one question: the picked option or the typed text, the points awarded, whether it was correct |
| `AttemptAnswerChoice` | The ticked options of a multiple-choice answer and the chosen pairs of a matching answer |

## Services

| Service | Responsibility |
|---|---|
| `IResourceService` | Search, filter, sort, paging; create, update, delete with ownership rules; duplicate check |
| `IResourceUploadService` | Validate an uploaded file (type by content, size, hash duplicate), store it and create the resource and its `ResourceFile` in one transaction |
| `IResourceImportService` | Parse and validate the CSV (`;` separated, UTF-8), match subjects and grades by name, find duplicates, import |
| `ICategoryService` | Subjects CRUD, unique names, no delete while in use |
| `IGradeLevelService` | Grades list, resource counts |
| `ILicenseService` | Licenses CRUD, URL and name validation, no delete while in use |
| `IFavoriteService` | Add, remove, paged list |
| `ICommentService` | Add, delete (author or administrator), paged list |
| `IReportService` | Create (one open report per user and content), moderation, delete reported content |
| `IOnlineLibraryService`, `ILibraryIconService` | Online library cards CRUD; download a library's site icon once and store it |
| `IResourceDiscoveryService` | "Find resources": search the fixed list of sources (one `IResourceSourceProvider` per source) and save the ticked suggestions through `IResourceService` |
| `ISectionService` | The question bank's section tree: create, rename, move (no cycles), delete when empty |
| `IQuestionBankService` | Questions in a section: create, edit, delete, move to another section, import from Aiken or Moodle XML |
| `IQuizService` | Create, edit, delete and publish quizzes (owner or administrator); validate questions of every type; grade practice; start, submit and grade exam attempts; record proctoring leaves; paged review of submissions |
| `IQuizItemService` | Compose a quiz from pinned questions and random draws, reorder the items, resolve them into the questions served to a student |
| `ISchoolClassService` | A teacher's classes |
| `IExamAssignmentService` | Assign a quiz to a class as an exam, generate its unique code, open and close it, release the results |
| `IQuestionExportService` | Write a section (with its subsections as categories) or a quiz as a Moodle XML file |
| `IUserService` | Roles, teacher approvals, account deletion |
| `IStatisticsService` | Numbers for the admin dashboard |
| `IFileStorage` | Stores and serves uploaded file bytes outside the database, chosen by configuration: `LocalFileStorage` on a disk volume, or `OciObjectStorage` in an S3-compatible bucket (OCI Object Storage) |
| `IMarkdownRenderer` | Renders uploaded Markdown to sanitized HTML (Markdig with raw HTML disabled, then HtmlSanitizer) |

Grading is a strategy per question type: each type has an `IQuestionGrader` (`SingleChoiceGrader`, `MultipleChoiceGrader`, `ShortAnswerGrader`, `NumericalGrader`, `MatchingGrader`, …) that turns a question and a submitted answer into the points earned. The graders are pure logic with no state, so `QuizGrading` only adds up their results, and a new type needs a new grader rather than changes to the others. Import and export of questions are pure as well: `AikenParser`, `MoodleXmlParser` and `MoodleXmlWriter`.

## Validation and security

- **Client and server validation**: data annotations with Bulgarian messages, jQuery validation in the browser, and the same rules enforced again in services.
- **Database constraints**: required fields and maximum lengths, unique indexes (license name, normalized resource URL), composite keys, foreign keys with restricted delete where data must not disappear.
- **SQL injection**: only EF Core and parameterized queries, no raw SQL.
- **XSS**: Razor encodes all output; links and iframes accept only `http` and `https`; user content is shown with `white-space: pre-line`. The one place HTML is emitted raw is uploaded Markdown, which is first rendered with raw HTML disabled and then run through an HTML sanitizer.
- **Uploads**: the file type is decided from the content (magic bytes for PDF and EPUB, a valid-UTF-8-text check for Markdown), never from the extension or the browser-supplied content type; a size limit and `RequestSizeLimit` cap the request; storage keys are generated (never a user-supplied name) and validated so a key cannot escape the storage folder; served files set `X-Content-Type-Options: nosniff` and allow framing only by this site.
- **CSRF**: `AutoValidateAntiforgeryToken` is applied globally to every unsafe request.
- **Parameter tampering**: view models instead of entity binding, ownership and role checks in services, `[Authorize(Roles = ...)]` on controllers and on the whole Admin area. Each exam attempt is addressed by an unguessable GUID token in the URL, not a sequential id, so one student cannot open or submit another's attempt.
- **Anonymous exams**: students sit exams without an account, so the start, submit and proctoring-ping endpoints are open — but they are POST actions that still validate the antiforgery token, and they change nothing a signed-in user owns. The exam timer, the single-attempt rule and the proctoring limit are enforced on the server (it holds the start time, the code and the leave count); the browser scripts only show the clock and send pings, and a web page cannot truly prevent switching tabs.
- **Quiz answers**: grading happens only on the server. The page a student solves never contains the correct answers: no accepted short answers, numbers or tolerances, and a matching question's lists carry answer texts in alphabetical order, never ids that would reveal the pairs. An option that does not belong to the question is treated as no answer. Short-answer patterns run with a regex timeout.
- **Outgoing requests**: the server only calls the fixed sources of "Find resources" and, for library icons, an address an administrator entered; `PublicNetworkGuard` allows public IP addresses only (checked after DNS and on every redirect), ports 80 and 443 only, with size and time limits, so the server cannot be made to reach its own network (SSRF).
- **Accounts**: the account locks for 5 minutes after 5 failed sign-ins.
- **Headers and cookies**: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and a Content-Security-Policy for framing, plugins, base URI and form targets; in Production every cookie is `Secure`; `AllowedHosts` limits the host names.
- **Size limits** on the CSV upload and the question import.

## Seeding

On start the app applies migrations and seeds reference data (only what is missing):

- grades 5 to 12,
- 31 subjects,
- 17 licenses (Creative Commons variants, public domain, MIT, Apache 2.0, GPL 2.0, the standard YouTube license, the GeoGebra license and others),
- 4 online libraries (only when the table is empty),
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

1778 tests (xUnit, Moq, EF Core InMemory). They cover every service (success and failure paths, including file upload validation, the file storage, the Markdown renderer, the resource sources with fake HTTP responses, and the full quiz flow: the question bank, every question type and its grader, import and export with import → export → import round trips, class exams, attempts and proctoring), seeders, DI registration, controllers (redirects, messages, permissions, file serving), security conventions, error pages and helpers.

Line coverage measured with coverlet (`dotnet test -c Release --collect:"XPlat Code Coverage"`):

| Part | Coverage |
|---|---|
| `SchoolLibrary.Services` (the logic the assignment asks for) | **97.0%** |
| `SchoolLibrary.Data` without generated migrations | 96.1% |
| `SchoolLibrary.Web` without compiled Razor views, Identity scaffolding and `Program.cs` | 92.8% |

The raw numbers per assembly are lower for Data (2.7%) and Web (51.9%) because generated migrations and compiled Razor views count as code; they are excluded above.

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
| `FileStorage__LocalPath` | Folder for uploaded files (a volume in Docker). Defaults to `App_Data/uploads` under the content root. Used when the provider is local |
| `FileStorage__Provider` | `Oci` keeps uploaded files in an S3-compatible bucket (OCI Object Storage), off the server's disk; any other value uses the local folder above |
| `FileStorage__Oci__Endpoint`, `__Region`, `__Bucket`, `__AccessKey`, `__SecretKey` | S3-compatible endpoint, region, bucket and Customer Secret Key, used only when the provider is `Oci` (secrets come from the environment and are never committed) |
| `YouTube__ApiKey` | YouTube Data API v3 key for "Find resources" (user secrets or environment, never committed). Without it YouTube is not offered |
| `AllowedHosts` | Host names the app answers to, separated by `;` (default `*`) |
| `Security__AllowInsecureCookies` | `true` only for a local stack opened over plain HTTP |
| `Seed__DemoData`, `Seed__DemoPassword` | Demo data, Development only |

## Deployment

The image is built for `linux/arm64` and `linux/amd64` (multi-stage Dockerfile). The production stack runs the app on `127.0.0.1:8080`; a reverse proxy (nginx) terminates TLS and forwards `Host`, `X-Forwarded-For` and `X-Forwarded-Proto`. PostgreSQL runs as a separate container on a shared Docker network. The Data Protection keys are kept on a named volume, so they survive container rebuilds and restarts. Uploaded files are kept on a named volume as well, or, by setting `FileStorage__Provider=Oci`, in an S3-compatible bucket (OCI Object Storage) so they live off the server's disk.

```
docker network create schoolnet
docker network connect schoolnet postgres
cp .env.example .env        # fill in the connection string and AllowedHosts, then chmod 600 .env
docker compose up -d --build
```

`scripts/backup-db.sh` dumps the database and removes old dumps; run it from cron.

## Why PostgreSQL

The application is deployed on an ARM64 (Ampere) server, which SQL Server does not support. PostgreSQL with Npgsql runs there natively, is free and works the same on a developer machine through Docker. There is a single database provider; tests use the EF Core InMemory provider.
