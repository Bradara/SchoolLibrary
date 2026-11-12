using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Areas.Identity.Pages.Account.Manage
{
    // Replaces the default Identity UI page: Bulgarian texts, keeps the user's resources
    // and does not let the last administrator delete their account.
    public class DeletePersonalDataModel : PageModel
    {
        private const string AdminRole = Roles.Administrator;

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public DeletePersonalDataModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required(ErrorMessage = "Въведете паролата си.")]
            [DataType(DataType.Password)]
            [Display(Name = "Парола")]
            public string Password { get; set; } = string.Empty;
        }

        public bool RequirePassword { get; set; }

        public bool IsLastAdmin { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Потребителят не е намерен.");
            }

            await LoadStateAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Потребителят не е намерен.");
            }

            await LoadStateAsync(user);

            if (IsLastAdmin)
            {
                ModelState.AddModelError(string.Empty,
                    "Вие сте единственият администратор. Дайте ролята на друг потребител, преди да изтриете профила си.");
                return Page();
            }

            if (RequirePassword)
            {
                if (!ModelState.IsValid)
                {
                    return Page();
                }

                if (!await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    ModelState.AddModelError("Input.Password", "Паролата не е вярна.");
                    return Page();
                }
            }

            // The database sets Resources.OwnerId to NULL, so the resources stay in the catalog
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Профилът не можа да бъде изтрит. Опитайте отново.");
                return Page();
            }

            await _signInManager.SignOutAsync();
            TempData["SuccessMessage"] = "Профилът ви е изтрит.";
            return Redirect("~/");
        }

        private async Task LoadStateAsync(ApplicationUser user)
        {
            RequirePassword = await _userManager.HasPasswordAsync(user);

            IsLastAdmin = false;
            if (await _userManager.IsInRoleAsync(user, AdminRole))
            {
                var admins = await _userManager.GetUsersInRoleAsync(AdminRole);
                IsLastAdmin = admins.Count <= 1;
            }
        }
    }
}
