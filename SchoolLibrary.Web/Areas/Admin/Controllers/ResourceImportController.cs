using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // Adds free resources from a CSV file: upload -> preview of every row -> confirm -> result
    public class ResourceImportController : AdminController
    {
        // Far above what the 512 KB text limit allows, so only huge uploads are refused before reading
        private const long MaxUploadBytes = 2 * 1024 * 1024;

        // The whole request (file or posted text plus the form fields): refused by the server beyond this
        private const int MaxRequestBytes = 3 * 1024 * 1024;

        private readonly IResourceImportService _import;
        private readonly ICategoryService _categories;
        private readonly ILicenseService _licenses;
        private readonly IGradeLevelService _gradeLevels;

        public ResourceImportController(
            IResourceImportService import,
            ICategoryService categories,
            ILicenseService licenses,
            IGradeLevelService gradeLevels)
        {
            _import = import;
            _categories = categories;
            _licenses = licenses;
            _gradeLevels = gradeLevels;
        }

        // GET: Admin/ResourceImport
        public async Task<IActionResult> Index()
        {
            return View(new ResourceImportIndexViewModel
            {
                Licenses = await GetLicenseNamesAsync(),
                Subjects = await GetSubjectNamesAsync(),
                Grades = (await _gradeLevels.GetAllAsync()).Select(g => g.Number.ToString()).ToList()
            });
        }

        // POST: Admin/ResourceImport/Preview
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxRequestBytes)]
        public async Task<IActionResult> Preview(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                return BackToUpload("Изберете CSV файл.");
            }

            if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return BackToUpload("Файлът трябва да е във формат CSV.");
            }

            if (file.Length > MaxUploadBytes)
            {
                return BackToUpload("Файлът е твърде голям.");
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var text = ResourceImportCsv.Decode(memory.ToArray());

            return View(new ResourceImportViewModel
            {
                Preview = await _import.PreviewAsync(text),
                CsvText = text,
                FileName = Path.GetFileName(file.FileName)
            });
        }

        // POST: Admin/ResourceImport/Import
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxRequestBytes)]
        public async Task<IActionResult> Import(string? csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                return BackToUpload("Няма данни за импортиране. Качете файла отново.");
            }

            return View("Result", new ResourceImportViewModel
            {
                Preview = await _import.ImportAsync(csvText)
            });
        }

        // GET: Admin/ResourceImport/Template
        public async Task<IActionResult> Template()
        {
            var file = ResourceImportCsv.BuildTemplateFile(await GetLicenseNamesAsync(), await GetSubjectNamesAsync());
            return File(file, "text/csv; charset=utf-8", ResourceImportCsv.TemplateFileName);
        }

        private IActionResult BackToUpload(string message)
        {
            TempData["ErrorMessage"] = message;
            return RedirectToAction(nameof(Index));
        }

        private async Task<IReadOnlyList<string>> GetLicenseNamesAsync() =>
            (await _licenses.GetOptionsAsync()).Select(l => l.Name).ToList();

        private async Task<IReadOnlyList<string>> GetSubjectNamesAsync() =>
            (await _categories.GetOptionsAsync()).Select(c => c.Name).ToList();
    }
}
