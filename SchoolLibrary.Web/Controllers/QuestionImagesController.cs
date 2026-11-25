using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Controllers
{
    // Pictures of a question, managed from its edit page (bank or quiz). The service checks that the
    // teacher may edit the question; every action goes back to the page it was sent from.
    [Authorize(Roles = Roles.TeacherOrAdministrator)]
    public class QuestionImagesController : Controller
    {
        // The picture limit plus room for the other form fields and the multipart framing
        private const long MaxRequestBytes = QuestionImage.MaxSizeBytes + 64 * 1024;

        private readonly IQuestionImageService _images;

        public QuestionImagesController(IQuestionImageService images)
        {
            _images = images;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        private bool IsAdministrator => User.IsInRole(Roles.Administrator);

        // POST: QuestionImages/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
        public async Task<IActionResult> Upload(int questionId, IFormFile? image, string? altText, string? returnUrl, CancellationToken cancellationToken)
        {
            if (image == null || image.Length == 0)
            {
                TempData["ErrorMessage"] = "Изберете изображение за качване.";
                return Back(returnUrl);
            }

            await using var content = image.OpenReadStream();
            var result = await _images.AddAsync(questionId,
                new UploadedFile(image.FileName, image.ContentType, image.Length, content),
                altText, CurrentUserId, IsAdministrator, cancellationToken);

            return Finish(result, returnUrl, image =>
                $"Изображението е добавено. Сложете {QuestionImageMarkers.Marker(image.Number)} в текста, за да изберете мястото му (иначе е под текста).");
        }

        // POST: QuestionImages/UpdateAltText/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAltText(int id, string? altText, string? returnUrl)
        {
            var result = await _images.UpdateAltTextAsync(id, altText, CurrentUserId, IsAdministrator);
            return Finish(result, returnUrl, _ => "Описанието е запазено.");
        }

        // POST: QuestionImages/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, string? returnUrl)
        {
            var result = await _images.DeleteAsync(id, CurrentUserId, IsAdministrator);
            return Finish(result, returnUrl, _ => "Изображението е премахнато.");
        }

        private IActionResult Finish(ServiceResult<QuestionImage> result, string? returnUrl, Func<QuestionImage, string> success)
        {
            switch (result.Error)
            {
                case ServiceError.NotFound:
                    return NotFound();
                case ServiceError.Forbidden:
                    return Forbid();
                case ServiceError.None:
                    TempData["SuccessMessage"] = success(result.Value!);
                    break;
                default:
                    TempData["ErrorMessage"] = result.Message;
                    break;
            }

            return Back(returnUrl);
        }

        // Only an address on this site, never one given by a crafted form (open redirect)
        private IActionResult Back(string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Index", "Sections");
    }
}
