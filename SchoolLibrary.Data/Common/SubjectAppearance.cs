namespace SchoolLibrary.Data.Common;

/// <summary>A Bootstrap Icons class with a label for the picker.</summary>
public sealed record SubjectIconOption(string Class, string Label);

/// <summary>A color from the subject palette with a label for the picker.</summary>
public sealed record SubjectColorOption(string Hex, string Label);

/// <summary>
/// The icon and color of a subject on the catalog tiles and cards. Both come from closed lists:
/// the values end up in a class attribute and in an inline CSS variable, so free text is never accepted.
/// </summary>
public static class SubjectAppearance
{
    public const string DefaultIcon = "bi-journal-bookmark";
    public const string DefaultColor = "#6c757d";

    public const int IconMaxLength = 40;
    public const int ColorMaxLength = 7;

    /// <summary>The same rules as the check constraints on the Categories table.</summary>
    public const string IconPattern = "^bi-[a-z0-9-]+$";
    public const string ColorPattern = "^#[0-9a-f]{6}$";

    public static IReadOnlyList<SubjectIconOption> Icons { get; } = new SubjectIconOption[]
    {
        new("bi-book", "Книга"),
        new("bi-chat-quote", "Цитат"),
        new("bi-translate", "Езици"),
        new("bi-pencil", "Писане"),
        new("bi-plus-slash-minus", "Аритметика"),
        new("bi-calculator", "Калкулатор"),
        new("bi-rulers", "Линии"),
        new("bi-graph-up", "Графика"),
        new("bi-cpu", "Процесор"),
        new("bi-laptop", "Лаптоп"),
        new("bi-code-slash", "Код"),
        new("bi-terminal", "Конзола"),
        new("bi-puzzle", "Пъзел"),
        new("bi-house-heart", "Дом"),
        new("bi-tree", "Дърво"),
        new("bi-flower1", "Цвете"),
        new("bi-heart-pulse", "Пулс"),
        new("bi-people", "Хора"),
        new("bi-hourglass-split", "Пясъчен часовник"),
        new("bi-globe-europe-africa", "Глобус"),
        new("bi-map", "Карта"),
        new("bi-bank", "Институция"),
        new("bi-shield-check", "Защита"),
        new("bi-lightbulb", "Идея"),
        new("bi-lightning-charge", "Мълния"),
        new("bi-magnet", "Магнит"),
        new("bi-stars", "Звезди"),
        new("bi-droplet", "Капка"),
        new("bi-music-note-beamed", "Музика"),
        new("bi-palette", "Палитра"),
        new("bi-brush", "Четка"),
        new("bi-camera-reels", "Филм"),
        new("bi-person-arms-up", "Танц"),
        new("bi-trophy", "Купа"),
        new("bi-bicycle", "Колело"),
        new("bi-tools", "Инструменти"),
        new("bi-briefcase", "Куфарче"),
        new("bi-sun", "Слънце"),
        new("bi-mortarboard", "Шапка"),
        new(DefaultIcon, "Тетрадка")
    };

    // Mid-tone colors: readable both on a light and on a dark background
    public static IReadOnlyList<SubjectColorOption> Colors { get; } = new SubjectColorOption[]
    {
        new("#2f6fed", "Синьо"),
        new("#0891b2", "Циан"),
        new("#0f9d8a", "Тюркоазено"),
        new("#2f9e44", "Зелено"),
        new("#5c940d", "Маслинено"),
        new("#b7791f", "Охра"),
        new("#e8590c", "Оранжево"),
        new("#e03131", "Червено"),
        new("#d6336c", "Розово"),
        new("#9c36b5", "Лилаво"),
        new("#6741d9", "Виолетово"),
        new(DefaultColor, "Сиво")
    };

    // The look of the standard subjects (the names from the seed). Used by the seed for a new database.
    private static readonly Dictionary<string, (string Icon, string Color)> Standard = new(StringComparer.Ordinal)
    {
        ["Български език и литература"] = ("bi-book", "#e03131"),
        ["Английски език"] = ("bi-translate", "#6741d9"),
        ["Немски език"] = ("bi-translate", "#b7791f"),
        ["Френски език"] = ("bi-translate", "#2f6fed"),
        ["Руски език"] = ("bi-translate", "#d6336c"),
        ["Испански език"] = ("bi-translate", "#e8590c"),
        ["Италиански език"] = ("bi-translate", "#2f9e44"),
        ["Математика"] = ("bi-plus-slash-minus", "#2f6fed"),
        ["Информатика"] = ("bi-cpu", "#0891b2"),
        ["Информационни технологии"] = ("bi-laptop", "#0891b2"),
        ["Компютърно моделиране"] = ("bi-code-slash", "#0f9d8a"),
        ["Компютърно моделиране и информационни технологии"] = ("bi-code-slash", "#0f9d8a"),
        ["Роден край"] = ("bi-house-heart", "#2f9e44"),
        ["Околен свят"] = ("bi-tree", "#2f9e44"),
        ["Човекът и обществото"] = ("bi-people", "#b7791f"),
        ["Човекът и природата"] = ("bi-flower1", "#5c940d"),
        ["История и цивилизации"] = ("bi-hourglass-split", "#b7791f"),
        ["География и икономика"] = ("bi-globe-europe-africa", "#2f9e44"),
        ["Философия"] = ("bi-lightbulb", "#6741d9"),
        ["Гражданско образование"] = ("bi-bank", "#2f6fed"),
        ["Биология и здравно образование"] = ("bi-heart-pulse", "#5c940d"),
        ["Физика и астрономия"] = ("bi-lightning-charge", "#e8590c"),
        ["Химия и опазване на околната среда"] = ("bi-droplet", "#9c36b5"),
        ["Музика"] = ("bi-music-note-beamed", "#d6336c"),
        ["Изобразително изкуство"] = ("bi-palette", "#e03131"),
        ["Технологии и предприемачество"] = ("bi-tools", DefaultColor),
        ["Предприемачество"] = ("bi-briefcase", "#b7791f"),
        ["Физическо възпитание и спорт"] = ("bi-trophy", "#e8590c"),
        ["Религия"] = ("bi-sun", "#b7791f"),
        ["Хореография"] = ("bi-person-arms-up", "#d6336c"),
        ["Друго"] = (DefaultIcon, DefaultColor)
    };

    public static bool IsValidIcon(string? icon) => icon != null && Icons.Any(i => i.Class == icon);

    public static bool IsValidColor(string? color) => color != null && Colors.Any(c => c.Hex == color);

    /// <summary>The look of a standard subject by its name; the default look for any other name.</summary>
    public static (string Icon, string Color) ForName(string? name) =>
        name != null && Standard.TryGetValue(name.Trim(), out var look) ? look : (DefaultIcon, DefaultColor);

    /// <summary>The names that have their own look (for the tests and the data migration).</summary>
    public static IReadOnlyCollection<string> StandardNames => Standard.Keys;
}
