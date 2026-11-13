using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using System.Security.Claims;

namespace SchoolLibrary.Controllers
{
    // Every signed-in user can comment; the list itself is shown on the resource details page
    [Authorize]
    public class CommentsController : Controller
    {
        private readonly ICommentService _comments;

        public CommentsController(ICommentService comments)
        {
            _comments = comments;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // POST: Comments/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int resourceId, string? text)
        {
            var result = await _comments.AddAsync(resourceId, CurrentUserId, text ?? string.Empty);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (!result.Succeeded)
            {
                // The page is shown again with the message and the text the user typed
                TempData["CommentError"] = result.Message;
                TempData["CommentDraft"] = text;
            }

            // The newest comment is on the first page
            return RedirectToDetails(resourceId, page: 1);
        }

        // POST: Comments/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int commentPage = 1)
        {
            var result = await _comments.DeleteAsync(id, CurrentUserId, User.IsInRole(Roles.Administrator));

            return result.Error switch
            {
                ServiceError.NotFound => NotFound(),
                ServiceError.Forbidden => Forbid(),
                _ => RedirectToDetails(result.Value!.ResourceId, commentPage)
            };
        }

        private IActionResult RedirectToDetails(int resourceId, int page) =>
            RedirectToAction("Details", "Resources", new { id = resourceId, commentPage = page }, "comments");
    }
}
