namespace GamaEdtech.Application.Service
{
    using System;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// AI help with school comments. The prompts are the ones the frontend used to send to its /api/chatgpt route,
    /// moved here so the OpenAI key and the prompt stay on the server and only signed-in users (admins for polishing)
    /// can use them.
    /// </summary>
    public sealed partial class SchoolAiService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<SchoolAiService>> localizer, Lazy<ILogger<SchoolAiService>> logger, Lazy<ISchoolService> schoolService,
        Lazy<IAiService> aiService)
        : LocalizableServiceBase<SchoolAiService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISchoolAiService
    {
        // The frontend's limit was 220; the JSON answer with a 400-character comment needs a bit more room.
        private const int DraftMaxTokens = 400;
        private const int PolishMaxTokens = 500;
        private const int MaxRejectCommentLength = 2000;

        public async Task<ResultData<SchoolAiCommentDto>> DraftCommentAsync(long schoolId)
        {
            try
            {
                var school = await schoolService.Value.GetSchoolAsync(new IdEqualsSpecification<School, long>(schoolId));
                if (school.OperationResult is not OperationResult.Succeeded || school.Data is null)
                {
                    return new(school.OperationResult) { Errors = school.Errors };
                }

                var completion = await aiService.Value.CompleteAsync(new()
                {
                    UserPrompt = BuildDraftPrompt(school.Data),
                    MaxTokens = DraftMaxTokens,
                });
                if (completion.OperationResult is not OperationResult.Succeeded || completion.Data?.Content is null)
                {
                    return new(OperationResult.Failed) { Errors = completion.Errors };
                }

                var draft = ParseDraft(completion.Data.Content);
                return draft is null
                    ? new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["SchoolAiCommentUnreadable"] },] }
                    : new(OperationResult.Succeeded) { Data = draft };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<string>> PolishRejectCommentAsync(string? comment)
        {
            try
            {
                var text = comment?.Trim();
                if (string.IsNullOrEmpty(text) || text.Length > MaxRejectCommentLength)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["SchoolAiRejectCommentInvalid"] },] };
                }

                var completion = await aiService.Value.CompleteAsync(new()
                {
                    UserPrompt = BuildPolishPrompt(text),
                    MaxTokens = PolishMaxTokens,
                });
                return completion.OperationResult is not OperationResult.Succeeded || string.IsNullOrWhiteSpace(completion.Data?.Content)
                    ? new(OperationResult.Failed) { Errors = completion.Errors }
                    : new(OperationResult.Succeeded) { Data = StripCodeFence(completion.Data.Content) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        // Same text as the frontend's useCommentAssistant prompt.
        internal static string BuildDraftPrompt(SchoolDto school)
        {
            var country = school.CountryTitle ?? string.Empty;
            var webSite = string.IsNullOrWhiteSpace(school.WebSite) ? string.Empty : $"- **Website:** {school.WebSite}";

            return $$"""
                You are an educational review assistant. Your task is to evaluate the following school and return a structured JSON response.

                ### **School Information:**
                - **Name:** {{school.Name}}
                - **Location:** {{country}}, {{school.StateTitle}}, {{school.CityTitle}}
                {{webSite}}

                ### **Evaluation Criteria:**
                Rate each of the following aspects on a scale of 1 to 5 stars (as numbers) using insights from sources like OpenStreetMap, Google Maps, and the school's official website. Then write a short, bold, and slightly provocative comment about the school.

                1. Quality of classrooms and educational facilities
                2. Teachers' proficiency and teaching effectiveness
                3. Access to and use of computers and technology
                4. Safety and overall atmosphere of the school
                5. Behavior of school officials towards students
                6. Affordability relative to the services provided
                7. Availability of suitable sports facilities
                8. Presence of art classes or counseling programs

                ### **Comment Rules:**
                - Write the **comment in the primary language used in** `{{country}}`. For example, use Persian for Iran, Italian for Italy, and French for France. **Do not use the user's IP address, browser language, or environment to determine the language.**
                - Mention at least **one clear strength and one clear weakness** of the school.
                - Use a **natural, human tone** with a **touch of challenge or controversy** to encourage discussion (e.g., “some parents might disagree…” or “not everyone would be happy with…”).
                - Keep the comment **short, engaging, and between 350 and 400 characters**.
                - The comment should **not repeat the school name or location**.
                - If it's a **kindergarten**, write like a thoughtful parent.
                  If it's an **educational group**, use inclusive and general language.
                - Always **end the comment with at least one emoji** to feel more personal and real.

                ### **Response Format (JSON):**
                Return a structured JSON object like this, with exactly 8 rating fields (1 to 5) and the comment:

                ```json
                {
                  "description": "🏫 Cornerstone Preparatory School offers a great learning environment with skilled teachers and strong safety measures. However, technology access and arts programs could be improved.",
                  "ratings": {
                    "classrooms_quality": 4,
                    "teachers_proficiency": 5,
                    "technology_access": 2,
                    "school_safety": 4,
                    "officials_behavior": 3,
                    "affordability": 4,
                    "sports_facilities": 3,
                    "art_counseling": 2
                  }
                }
                ```
                """;
        }

        // Same text as the frontend's admin contribution detail prompt.
        internal static string BuildPolishPrompt(string comment) => string.Join('\n',
            "You are a professional English writing editor for a school contribution rejection comment.",
            "Polish the admin comment below.",
            "Improve grammar, spelling, word choice, clarity, and professional tone.",
            "Preserve the original meaning and intent exactly.",
            "Do not add new facts, promises, links, timelines, policies, or technical details.",
            "Remove or soften any rude, offensive, aggressive, or unprofessional wording while keeping the message clear.",
            "Return plain text only.",
            "Do not use Markdown.",
            "Do not wrap the response in a fenced code block.",
            string.Empty,
            "Admin comment:",
            comment);

        /// <summary>The model's JSON answer as a draft, or null when it can't be read. Ratings are kept to 1-5.</summary>
        internal static SchoolAiCommentDto? ParseDraft(string content)
        {
            try
            {
                using var document = JsonDocument.Parse(StripCodeFence(content));
                var root = document.RootElement;
                if (root.ValueKind is not JsonValueKind.Object
                    || !root.TryGetProperty("description", out var description) || description.ValueKind is not JsonValueKind.String
                    || !root.TryGetProperty("ratings", out var ratings) || ratings.ValueKind is not JsonValueKind.Object)
                {
                    return null;
                }

                var comment = description.GetString()?.Trim();
                if (string.IsNullOrEmpty(comment))
                {
                    return null;
                }

                int Rate(string name) => ratings.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number
                    ? (int)Math.Clamp(Math.Round(value.GetDouble()), 1, 5)
                    : 3;

                return new()
                {
                    Comment = comment,
                    ClassesQualityRate = Rate("classrooms_quality"),
                    EducationRate = Rate("teachers_proficiency"),
                    ITTrainingRate = Rate("technology_access"),
                    SafetyAndHappinessRate = Rate("school_safety"),
                    BehaviorRate = Rate("officials_behavior"),
                    TuitionRatioRate = Rate("affordability"),
                    FacilitiesRate = Rate("sports_facilities"),
                    ArtisticActivitiesRate = Rate("art_counseling"),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Models often wrap the answer in a fence (```json, ```text...) despite being asked not to.</summary>
        internal static string StripCodeFence(string text) => CodeFence().Replace(text.Trim(), string.Empty).Trim();

        [GeneratedRegex(@"^```[a-zA-Z]*\s*|\s*```$", RegexOptions.None, 1000)]
        private static partial Regex CodeFence();
    }
}
