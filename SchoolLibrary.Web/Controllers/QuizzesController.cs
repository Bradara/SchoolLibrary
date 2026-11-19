using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Controllers
{
    // Practice quizzes and exams are open to everyone, including visitors who are not signed in.
    // The exam endpoints create and grade an attempt anonymously by design (students are not
    // registered); every write still checks the anti-forgery token.
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
            var result = await _quizzes.GetPracticeSetAsync(id);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return View(result.Value);
        }

        // POST: Quizzes/Solve/5 — grade the answers and show the result
        // Answers arrive as answers[questionId] = optionId; questionIds carries the shown set (for random draws).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Solve(int id, [FromForm] List<int>? questionIds, [FromForm] Dictionary<int, int?>? answers)
        {
            var result = await _quizzes.GradePracticeAsync(
                id, questionIds ?? new List<int>(), answers ?? new Dictionary<int, int?>());
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return View("Result", result.Value);
        }

        // GET: Quizzes/Exam — enter an exam by its code
        [HttpGet]
        public IActionResult Exam() => View(new ExamEntryViewModel());

        // POST: Quizzes/Exam
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Exam(ExamEntryViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var result = await _quizzes.StartExamAsync(vm.Code, vm.StudentName, vm.StudentNumber);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(vm);
            }

            return RedirectToAction(nameof(Take), new { token = result.Value!.Token });
        }

        // GET: Quizzes/Take/{token} — answer the exam
        [HttpGet]
        public async Task<IActionResult> Take(Guid token)
        {
            var attempt = await _quizzes.GetExamAttemptAsync(token);
            if (attempt == null)
            {
                return NotFound();
            }

            if (attempt.SubmittedOn != null)
            {
                return RedirectToAction(nameof(ExamResult), new { token });
            }

            return View(new ExamTakeViewModel { Attempt = attempt, RemainingSeconds = RemainingSeconds(attempt) });
        }

        // POST: Quizzes/Take/{token}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Take(Guid token, [FromForm] Dictionary<int, int?>? answers)
        {
            var result = await _quizzes.SubmitExamAsync(token, answers ?? new Dictionary<int, int?>());
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return RedirectToAction(nameof(ExamResult), new { token });
        }

        // POST: Quizzes/RecordLeave/{token} — a proctoring ping sent by the exam page (fetch)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordLeave(Guid token)
        {
            var result = await _quizzes.RecordLeaveAsync(token);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            var status = result.Value!;
            return Json(new { leaveCount = status.LeaveCount, flagged = status.Flagged, shouldSubmit = status.ShouldSubmit });
        }

        // GET: Quizzes/ExamResult/{token}
        public async Task<IActionResult> ExamResult(Guid token)
        {
            var result = await _quizzes.GetExamResultAsync(token);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (!result.Succeeded)
            {
                // Not submitted yet — send the student back to finish answering
                return RedirectToAction(nameof(Take), new { token });
            }

            var attempt = await _quizzes.GetExamAttemptAsync(token);
            return View(new ExamResultViewModel
            {
                Result = result.Value!,
                StudentName = attempt?.StudentName ?? string.Empty,
                SubmittedOn = attempt?.SubmittedOn
            });
        }

        // Seconds left for a timed exam; null when the exam has no time limit
        private static int? RemainingSeconds(QuizAttempt attempt)
        {
            if (attempt.Quiz.TimeLimitMinutes is not int minutes)
            {
                return null;
            }

            var elapsed = (DateTime.UtcNow - attempt.StartedOn).TotalSeconds;
            return Math.Max(0, minutes * 60 - (int)elapsed);
        }
    }
}
