using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Markdown;
using SchoolLibrary.Services.Reading;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Storage;
using SchoolLibrary.ViewModels;
using System.Security.Claims;
using System.Text;

namespace SchoolLibrary.Controllers
{
    [Authorize]
    public class ResourcesController : Controller
    {
        // The whole upload request (the file plus the form fields): refused by the server beyond this
        private const long MaxUploadRequestBytes = UploadConstraints.MaxFileSizeBytes + 1024 * 1024;

        private readonly IResourceService _resources;
        private readonly IResourceUploadService _uploads;
        private readonly IFileStorage _storage;
        private readonly IMarkdownRenderer _markdown;
        private readonly ICategoryService _categories;
        private readonly IGradeLevelService _gradeLevels;
        private readonly ILicenseService _licenses;
        private readonly IFavoriteService _favorites;
        private readonly ICommentService _comments;
        private readonly IReadingPositionService _reading;
        private readonly IBookmarkService _bookmarks;
        private readonly IHighlightService _highlights;

        public ResourcesController(
            IResourceService resources,
            IResourceUploadService uploads,
            IFileStorage storage,
            IMarkdownRenderer markdown,
            ICategoryService categories,
            IGradeLevelService gradeLevels,
            ILicenseService licenses,
            IFavoriteService favorites,
            ICommentService comments,
            IReadingPositionService reading,
            IBookmarkService bookmarks,
            IHighlightService highlights)
        {
            _resources = resources;
            _uploads = uploads;
            _storage = storage;
            _markdown = markdown;
            _categories = categories;
            _gradeLevels = gradeLevels;
            _licenses = licenses;
            _favorites = favorites;
            _comments = comments;
            _reading = reading;
            _bookmarks = bookmarks;
            _highlights = highlights;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        private bool IsSignedIn => User.Identity?.IsAuthenticated == true;

        private bool IsAdministrator => User.IsInRole(Roles.Administrator);

        // Who is looking: decides which resources are listed and opened
        private ResourceViewer Viewer => ResourceViewer.For(User);

        // GET: Resources
        [AllowAnonymous]
        public async Task<IActionResult> Index(
            ResourceKind? kind,
            string? searchTitle,
            int? categoryId,
            int? gradeLevelId,
            string? ownerId,
            DateTime? fromDate,
            DateTime? toDate,
            ResourceSortOrder? sortOrder,
            int page = 1)
        {
            var result = await _resources.SearchAsync(new ResourceFilter
            {
                Kind = kind,
                SearchText = searchTitle,
                CategoryId = categoryId,
                GradeLevelId = gradeLevelId,
                OwnerId = ownerId,
                FromDate = fromDate,
                ToDate = toDate,
                SortOrder = sortOrder ?? ResourceSortOrder.DateDesc,
                Page = page,
                Viewer = Viewer
            });

            var owners = await _resources.GetOwnersAsync(Viewer);

            // The hearts of the page: only signed-in users have favorites
            var favoriteIds = IsSignedIn
                ? await _favorites.GetFavoriteIdsAsync(CurrentUserId, result.Items.Select(r => r.Id))
                : new HashSet<int>();

            var vm = new ResourceIndexViewModel
            {
                Kind = kind,
                SearchTitle = searchTitle,
                CategoryId = categoryId,
                GradeLevelId = gradeLevelId,
                OwnerId = ownerId,
                FromDate = fromDate,
                ToDate = toDate,
                SortOrder = sortOrder ?? ResourceSortOrder.DateDesc,

                Categories = await _categories.GetOptionsAsync(),
                GradeLevels = await _gradeLevels.GetAllAsync(),
                Owners = owners.Select(o => new SelectListItem { Value = o.Id, Text = o.FullName }).ToList(),

                Resources = result.Items,
                FavoriteIds = favoriteIds,
                Page = result.Page,
                TotalPages = result.TotalPages,
                TotalCount = result.TotalCount
            };

            return View(vm);
        }

        // GET: Resources/Details
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id, int commentPage = 1)
        {
            if (id == null)
            {
                return NotFound();
            }

            // A hidden resource looks the same as a missing one, so its existence is not revealed
            var resource = await _resources.GetDetailsAsync(id.Value, Viewer);
            if (resource == null)
            {
                return NotFound();
            }

            var comments = await _comments.GetForResourceAsync(resource.Id, commentPage);

            // A signed-in reader continues a book where they stopped, on whichever device they read it
            var readingPosition = IsSignedIn && resource.File?.ContentType == ReadingPositionService.EpubContentType
                ? await _reading.GetAsync(CurrentUserId, resource.Id)
                : null;

            return View(new ResourceDetailsViewModel
            {
                Resource = resource,
                MarkdownHtml = await RenderMarkdownAsync(resource),
                IsFavorite = IsSignedIn && await _favorites.IsFavoriteAsync(CurrentUserId, resource.Id),
                FavoriteCount = await _favorites.CountAsync(resource.Id),
                Comments = comments.Items,
                CommentPage = comments.Page,
                CommentTotalPages = comments.TotalPages,
                CommentTotal = comments.TotalCount,
                CommentError = TempData["CommentError"] as string,
                CommentDraft = TempData["CommentDraft"] as string,
                ReadingPosition = readingPosition,
                CanChangeAudience = IsSignedIn && ResourcePermissions.CanEdit(resource, CurrentUserId, IsAdministrator)
            });
        }

        // POST: Resources/SetAudience/5 — the owner changes who may see the resource (from the details page)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAudience(int id, List<ResourceAudience>? audience)
        {
            var result = await _resources.SetAudienceAsync(id, ResourceVisibility.Combine(audience), CurrentUserId, IsAdministrator);
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            TempData["SuccessMessage"] = $"Видимост: {ResourceVisibility.Describe(result.Value!.Audience)}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Resources/SaveReadingPosition/5 — the EPUB reader saves where the user is (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveReadingPosition(int id, string? cfi, int percent)
        {
            var result = await _reading.SaveAsync(CurrentUserId, id, cfi, percent);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return result.Succeeded
                ? Json(new { ok = true })
                : BadRequest(new { ok = false, message = result.Message });
        }

        // GET: Resources/Bookmarks/5 — the signed-in user's bookmarks in this book (fetch, JSON)
        [HttpGet]
        public async Task<IActionResult> Bookmarks(int id)
        {
            var list = await _bookmarks.GetAsync(CurrentUserId, id);
            return Json(list.Select(b => new { id = b.Id, cfi = b.Cfi, label = b.Label, percent = b.Percent }));
        }

        // POST: Resources/AddBookmark/5 — marks the page the reader is on (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddBookmark(int id, string? cfi, string? label, int percent)
        {
            var result = await _bookmarks.AddAsync(CurrentUserId, id, cfi, label, percent);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (!result.Succeeded)
            {
                return BadRequest(new { ok = false, message = result.Message });
            }

            var b = result.Value!;
            return Json(new { ok = true, bookmark = new { id = b.Id, cfi = b.Cfi, label = b.Label, percent = b.Percent } });
        }

        // POST: Resources/DeleteBookmark — removes one of the user's own bookmarks (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBookmark(int bookmarkId)
        {
            var result = await _bookmarks.RemoveAsync(CurrentUserId, bookmarkId);
            return result.Succeeded ? Json(new { ok = true }) : NotFound();
        }

        // GET: Resources/Highlights/5 — the signed-in user's highlights and notes in this book (fetch, JSON)
        [HttpGet]
        public async Task<IActionResult> Highlights(int id)
        {
            var list = await _highlights.GetAsync(CurrentUserId, id);
            return Json(list.Select(ToJson));
        }

        // POST: Resources/AddHighlight/5 — highlights the selected passage (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddHighlight(int id, string? cfi, string? text, string? color, string? note, int percent)
            => HighlightResponse(await _highlights.AddAsync(CurrentUserId, id, new HighlightInput(cfi, text, color, note, percent)));

        // POST: Resources/UpdateHighlight — changes the color and the note (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateHighlight(int highlightId, string? color, string? note)
            => HighlightResponse(await _highlights.UpdateAsync(CurrentUserId, highlightId, color, note));

        // POST: Resources/DeleteHighlight — removes one of the user's own highlights (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHighlight(int highlightId)
        {
            var result = await _highlights.RemoveAsync(CurrentUserId, highlightId);
            return result.Succeeded ? Json(new { ok = true }) : NotFound();
        }

        private IActionResult HighlightResponse(ServiceResult<HighlightInfo> result)
        {
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return result.Succeeded
                ? Json(new { ok = true, highlight = ToJson(result.Value!) })
                : BadRequest(new { ok = false, message = result.Message });
        }

        private static object ToJson(HighlightInfo h) =>
            new { id = h.Id, cfi = h.Cfi, text = h.Text, color = h.Color, note = h.Note, percent = h.Percent };

        // GET: Resources/Download — serves an uploaded file for preview (inline) or download
        [AllowAnonymous]
        public async Task<IActionResult> Download(int id, bool download = false)
        {
            var file = await _resources.GetFileAsync(id, Viewer);
            if (file == null)
            {
                return NotFound();
            }

            var stream = await _storage.OpenReadAsync(file.StorageKey);
            if (stream == null)
            {
                return NotFound();
            }

            // The global policy forbids all framing; relax it so this site's own details page can show
            // the file in a frame (for example the PDF preview), while other sites still cannot frame it.
            Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";

            // nosniff is set globally; the content type was validated from the bytes when the file was uploaded.
            // Range processing lets the browser seek inside a PDF.
            return download
                ? File(stream, file.ContentType, file.FileName, enableRangeProcessing: true)
                : File(stream, file.ContentType, enableRangeProcessing: true);
        }

        // GET: Resources/Create
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Create(ResourceKind kind = ResourceKind.Lesson)
        {
            var vm = new ResourceFormViewModel { Kind = kind };
            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Create(ResourceFormViewModel vm)
        {
            ValidateSelections(vm);

            if (ModelState.IsValid)
            {
                var result = await _resources.CreateAsync(ToInput(vm), CurrentUserId);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = $"Ресурсът {result.Value!.Title} е създаден успешно.";
                    return RedirectToAction(nameof(Index));
                }

                await AddFailureAsync(vm, result);
            }

            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // GET: Resources/Upload
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Upload()
        {
            var vm = new ResourceUploadViewModel();
            await LoadUploadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        [RequestSizeLimit(MaxUploadRequestBytes)]
        public async Task<IActionResult> Upload(ResourceUploadViewModel vm)
        {
            ValidateUploadSelections(vm);

            if (ModelState.IsValid)
            {
                await using var stream = vm.File!.OpenReadStream();
                var file = new UploadedFile(vm.File.FileName, vm.File.ContentType, vm.File.Length, stream);

                var result = await _uploads.UploadAsync(ToUploadInput(vm), file, CurrentUserId);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = $"Материалът {result.Value!.Title} е качен успешно.";
                    return RedirectToAction(nameof(Index), new { kind = ResourceKind.Upload });
                }

                ModelState.AddModelError(string.Empty, result.Message!);
            }

            await LoadUploadOptionsAsync(vm);
            return View(vm);
        }

        // GET: Resources/Edit
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var result = await _resources.GetForEditAsync(id.Value, CurrentUserId, IsAdministrator);
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            var resource = result.Value!;
            var vm = new ResourceFormViewModel
            {
                Id = resource.Id,
                Title = resource.Title,
                Author = resource.Author,
                Source = resource.Source,
                Description = resource.Description,
                Url = resource.Url,
                Kind = resource.Kind,
                Audience = ResourceVisibility.Groups.Select(g => g.Group).Where(g => resource.Audience.HasFlag(g)).ToList(),
                LicenseId = resource.LicenseId,
                SelectedCategoryIds = resource.Categories.Select(c => c.Id).ToList(),
                SelectedGradeLevelIds = resource.GradeLevels.Select(g => g.Id).ToList()
            };
            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Edit(int id, ResourceFormViewModel vm)
        {
            if (id != vm.Id)
            {
                return NotFound();
            }

            ValidateSelections(vm);

            if (ModelState.IsValid)
            {
                var result = await _resources.UpdateAsync(id, ToInput(vm), CurrentUserId, IsAdministrator);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = $"Ресурсът {result.Value!.Title} е редактиран успешно.";
                    return RedirectToAction(nameof(Index));
                }

                if (result.Error != ServiceError.Invalid && result.Error != ServiceError.Conflict)
                {
                    return ToActionResult(result);
                }

                await AddFailureAsync(vm, result);
            }

            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // GET: Resources/Delete
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var result = await _resources.GetForDeleteAsync(id.Value, CurrentUserId, IsAdministrator);
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            return View(result.Value);
        }

        // POST: Resources/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // Read the file before deleting, so its bytes can be removed from storage afterwards
            var file = await _resources.GetFileAsync(id, Viewer);

            var result = await _resources.DeleteAsync(id, CurrentUserId, IsAdministrator);
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            // The database row is gone (cascade); now remove the stored file so nothing is orphaned
            if (file != null)
            {
                await _storage.DeleteAsync(file.StorageKey);
            }

            TempData["SuccessMessage"] = $"Ресурсът {result.Value!.Title} е изтрит успешно.";
            return RedirectToAction(nameof(Index));
        }

        // Maps a failed service result to the matching HTTP response
        private IActionResult ToActionResult(ServiceResult result) => result.Error switch
        {
            ServiceError.NotFound => NotFound(),
            ServiceError.Forbidden => Forbid(),
            _ => BadRequest(result.Message)
        };

        // Shows the failure on the form; for a duplicate link also remembers which resource already has it
        private async Task AddFailureAsync(ResourceFormViewModel vm, ServiceResult result)
        {
            ModelState.AddModelError(string.Empty, result.Message!);

            if (result.Error == ServiceError.Conflict)
            {
                vm.DuplicateOfId = (await _resources.FindByUrlAsync(vm.Url))?.Id;
            }
        }

        private static ResourceInput ToInput(ResourceFormViewModel vm) => new()
        {
            Title = vm.Title,
            Author = vm.Author,
            Source = vm.Source,
            Description = vm.Description,
            Url = vm.Url,
            Kind = vm.Kind,
            Audience = ResourceVisibility.Combine(vm.Audience),
            LicenseId = vm.LicenseId,
            CategoryIds = vm.SelectedCategoryIds,
            GradeLevelIds = vm.SelectedGradeLevelIds
        };

        private async Task LoadOptionsAsync(ResourceFormViewModel vm)
        {
            vm.AllCategories = (await _categories.GetOptionsAsync()).ToList();
            vm.AllGradeLevels = (await _gradeLevels.GetAllAsync()).ToList();
            vm.AllLicenses = (await _licenses.GetOptionsAsync()).ToList();
        }

        // Reads an uploaded Markdown file and renders it to sanitized HTML for inline display
        private async Task<string?> RenderMarkdownAsync(Data.Models.Resource resource)
        {
            if (resource.Kind != ResourceKind.Upload
                || resource.File == null
                || !resource.File.ContentType.StartsWith("text/markdown", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var stream = await _storage.OpenReadAsync(resource.File.StorageKey);
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            return _markdown.RenderToHtml(text);
        }

        private async Task LoadUploadOptionsAsync(ResourceUploadViewModel vm)
        {
            vm.AllCategories = (await _categories.GetOptionsAsync()).ToList();
            vm.AllGradeLevels = (await _gradeLevels.GetAllAsync()).ToList();
            vm.AllLicenses = (await _licenses.GetOptionsAsync()).ToList();
        }

        private static ResourceInput ToUploadInput(ResourceUploadViewModel vm) => new()
        {
            Title = vm.Title,
            Author = vm.Author,
            Source = vm.Source,
            Description = vm.Description,
            Kind = ResourceKind.Upload,
            Audience = ResourceVisibility.Combine(vm.Audience),
            LicenseId = vm.LicenseId,
            CategoryIds = vm.SelectedCategoryIds,
            GradeLevelIds = vm.SelectedGradeLevelIds
        };

        // Keeps the errors next to the file input and the checkbox groups; the service validates again
        private void ValidateUploadSelections(ResourceUploadViewModel vm)
        {
            if (vm.File == null || vm.File.Length == 0)
            {
                ModelState.AddModelError(nameof(vm.File), "Изберете файл за качване.");
            }
            if (vm.SelectedCategoryIds.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.SelectedCategoryIds), "Изберете поне един предмет.");
            }
            if (vm.SelectedGradeLevelIds.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.SelectedGradeLevelIds), "Изберете поне един клас.");
            }
        }

        // Keeps the error next to the checkbox groups; the service validates the same rules again
        private void ValidateSelections(ResourceFormViewModel vm)
        {
            if (vm.SelectedCategoryIds.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.SelectedCategoryIds), "Изберете поне един предмет.");
            }
            if (vm.SelectedGradeLevelIds.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.SelectedGradeLevelIds), "Изберете поне един клас.");
            }
        }
    }
}
