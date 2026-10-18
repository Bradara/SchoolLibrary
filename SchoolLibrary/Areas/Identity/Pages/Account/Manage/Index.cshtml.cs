using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _context;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        public string Email { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;

        public int ResourceCount { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required(ErrorMessage = "Въведете име.")]
            [MaxLength(50, ErrorMessage = "Името може да е до {1} знака.")]
            public string FirstName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Въведете фамилия.")]
            [MaxLength(50, ErrorMessage = "Фамилията може да е до {1} знака.")]
            public string LastName { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            await LoadAccountInfoAsync(user);
            Input = new InputModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await LoadAccountInfoAsync(user);
                return Page();
            }

            user.FirstName = Input.FirstName.Trim();
            user.LastName = Input.LastName.Trim();

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Профилът не можа да бъде обновен. Опитайте отново.");
                await LoadAccountInfoAsync(user);
                return Page();
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Профилът е обновен.";
            return RedirectToPage();
        }

        private async Task LoadAccountInfoAsync(ApplicationUser user)
        {
            Email = user.Email ?? string.Empty;
            UserId = user.Id;
            ResourceCount = await _context.Resources.CountAsync(r => r.OwnerId == user.Id);
        }
    }
}