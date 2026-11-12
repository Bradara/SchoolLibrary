using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<RegisterModel> _logger;

        public RegisterModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ILogger<RegisterModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Име")]
            public string FirstName { get; set; }

            [Required]
            [Display(Name = "Фамилия")]
            public string LastName { get; set; }

            [Required(ErrorMessage = "Въведете имейл.")]
            [EmailAddress(ErrorMessage = "Невалиден имейл адрес.")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Въведете парола.")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Паролата трябва да е поне {2} знака.")]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Compare("Password", ErrorMessage = "Паролите не съвпадат.")]
            public string ConfirmPassword { get; set; }

            [EnumDataType(typeof(AccountType), ErrorMessage = "Изберете тип профил.")]
            [Display(Name = "Аз съм")]
            public AccountType AccountType { get; set; } = AccountType.Student;
        }

        public void OnGet(string returnUrl = null)
        {
            Input = new InputModel();
            ReturnUrl = returnUrl ?? Url.Content("~/");
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            if (ModelState.IsValid)
            {
                // The first registered user becomes the administrator, so a fresh installation never needs a restart
                var isFirstUser = (await _userManager.GetUsersInRoleAsync(Roles.Administrator)).Count == 0;

                var user = new ApplicationUser
                {
                    UserName = Input.Email,
                    Email = Input.Email,
                    FirstName = Input.FirstName,
                    LastName = Input.LastName,
                    TeacherRequested = !isFirstUser && Input.AccountType == AccountType.Teacher
                };
                var result = await _userManager.CreateAsync(user, Input.Password);
                if (result.Succeeded)
                {
                    _logger.LogInformation("Създаден е нов профил.");

                    await AssignRolesAsync(user, isFirstUser);

                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return LocalRedirect(returnUrl);
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private async Task AssignRolesAsync(ApplicationUser user, bool isFirstUser)
        {
            await _userManager.AddToRoleAsync(user, Roles.User);

            if (isFirstUser)
            {
                await _userManager.AddToRoleAsync(user, Roles.Administrator);
                return;
            }

            // A teacher request starts with student rights until an administrator confirms it
            var role = Input.AccountType switch
            {
                AccountType.Parent => Roles.Parent,
                _ => Roles.Student
            };
            await _userManager.AddToRoleAsync(user, role);
        }
    }
}
