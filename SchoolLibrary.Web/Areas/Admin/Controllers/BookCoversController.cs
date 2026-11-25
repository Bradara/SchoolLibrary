using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // Takes the covers out of the books uploaded before covers were kept (the button on the dashboard)
    public class BookCoversController : AdminController
    {
        private readonly IResourceCoverService _covers;

        public BookCoversController(IResourceCoverService covers)
        {
            _covers = covers;
        }

        // POST: Admin/BookCovers/Extract
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Extract(CancellationToken cancellationToken)
        {
            var result = await _covers.ExtractMissingAsync(cancellationToken);

            if (result.Checked == 0)
            {
                TempData["SuccessMessage"] = "Всички книги вече имат обложки.";
            }
            else
            {
                var message = $"Проверени книги: {result.Checked}. Добавени обложки: {result.Added}.";
                if (result.WithoutCover > 0)
                {
                    message += $" Без обложка във файла: {result.WithoutCover}.";
                }

                if (result.FileMissing > 0)
                {
                    message += $" Липсващ файл: {result.FileMissing}.";
                }

                TempData[result.FileMissing > 0 ? "ErrorMessage" : "SuccessMessage"] = message;
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
