using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Controllers
{
    // Practice quizzes are open to everyone, including visitors who are not signed in
    [AllowAnonymous]
    public class QuizzesController : Controller
    {
        private readonly IQuizService _quizzes;

        public QuizzesController(IQuizService quizzes)
        {
            _quizzes = quizzes;
        }

        // GET: Quizzes — the list of published practice quizzes
        public async Task<IActionResult> Index(int page = 1)
        {
            var result = await _quizzes.GetPublishedPracticeAsync(page);
            return View(result);
        }

        // GET: Quizzes/Solve/5 — the questions to answer
        public async Task<IActionResult> Solve(int id)
        {
            var quiz = await _quizzes.GetPracticeForSolvingAsync(id);
            if (quiz == null)
            {
                return NotFound();
            }

            return View(quiz);
        }

        // POST: Quizzes/Solve/5 — grade the answers and show the result
        // Answers arrive as answers[questionId] = optionId; an unanswered question has no entry.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Solve(int id, [FromForm] Dictionary<int, int?>? answers)
        {
            var result = await _quizzes.GradePracticeAsync(id, answers ?? new Dictionary<int, int?>());
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return View("Result", result.Value);
        }
    }
}
