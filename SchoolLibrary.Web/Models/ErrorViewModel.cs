namespace SchoolLibrary.Models
{
    /// <summary>What the error page shows. Built from the HTTP status code, never from exception details.</summary>
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public int StatusCode { get; set; } = 500;

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Icon { get; set; } = "bi-emoji-frown";

        /// <summary>True for "page not found": the page then offers links to useful places.</summary>
        public bool IsNotFound => StatusCode == 404;

        // The request id helps to find a server error in the log; for the other codes it is just noise
        public bool ShowRequestId => StatusCode >= 500 && !string.IsNullOrEmpty(RequestId);

        /// <summary>
        /// The page for a status code. Only client and server errors (400-599) have their own text;
        /// anything else (including a direct visit to the error address) is shown as a server error.
        /// </summary>
        public static ErrorViewModel For(int? statusCode, string? requestId)
        {
            var code = statusCode is >= 400 and <= 599 ? statusCode.Value : 500;

            var model = code switch
            {
                404 => new ErrorViewModel
                {
                    Title = "Страницата не е намерена",
                    Message = "Адресът може да е грешен или материалът да е бил изтрит.",
                    Icon = "bi-search"
                },
                403 => new ErrorViewModel
                {
                    Title = "Нямате достъп",
                    Message = "Нямате права да отворите тази страница.",
                    Icon = "bi-shield-lock"
                },
                400 => new ErrorViewModel
                {
                    Title = "Невалидна заявка",
                    Message = "Заявката не може да бъде обработена. Опитайте отново.",
                    Icon = "bi-exclamation-triangle"
                },
                >= 500 => new ErrorViewModel
                {
                    Title = "Нещо се обърка",
                    Message = "Съжаляваме, но не успяхме да обработим Вашата заявка. Опитайте отново след малко.",
                    Icon = "bi-emoji-frown"
                },
                _ => new ErrorViewModel
                {
                    Title = "Заявката не може да бъде изпълнена",
                    Message = "Опитайте отново или се върнете в началото.",
                    Icon = "bi-exclamation-triangle"
                }
            };

            model.StatusCode = code;
            model.RequestId = requestId;
            return model;
        }
    }
}
