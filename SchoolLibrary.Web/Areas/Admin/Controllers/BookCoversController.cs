using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // Covers for the files uploaded before covers were kept: books (taken out of the EPUB on the server) and PDF
    // files (the first page drawn by the administrator's browser with pdf.js and sent here, one at a time)
    public class BookCoversController : AdminController
    {
        // The largest request with a picture: the picture limit and room for the form fields
        private const long MaxPictureRequestBytes = ResourceCovers.MaxPdfCoverBytes + 64 * 1024;

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

        // GET: Admin/BookCovers/Pdfs — the PDF files without a cover; the page draws them (sl-pdf-covers-admin.js)
        public async Task<IActionResult> Pdfs(CancellationToken cancellationToken)
        {
            return View(await _covers.GetPdfsWithoutCoverAsync(cancellationToken));
        }

        // POST: Admin/BookCovers/SetPdfCover/5 — the first page of one PDF (fetch, JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxPictureRequestBytes)]
        public async Task<IActionResult> SetPdfCover(int id, IFormFile? picture, CancellationToken cancellationToken)
        {
            byte[]? bytes = null;
            if (picture != null && picture.Length is > 0 and <= ResourceCovers.MaxPdfCoverBytes)
            {
                using var buffer = new MemoryStream();
                await picture.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }

            var result = await _covers.SetPdfCoverAsync(id, bytes, cancellationToken);
            return result.Error switch
            {
                ServiceError.None => Json(new { ok = true }),
                ServiceError.NotFound => NotFound(new { ok = false, message = result.Message }),
                ServiceError.Conflict => Conflict(new { ok = false, message = result.Message }),
                _ => BadRequest(new { ok = false, message = result.Message })
            };
        }
    }
}
