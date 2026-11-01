namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Data.Dto.School;

    using Xunit;

    /// <summary>The AI school comment helper: the prompt is built from the school's own data, and the answer is read safely.</summary>
    public class SchoolAiServiceTests
    {
        private const string Answer = """
            ```json
            {
              "description": "Strong teachers, weak labs 🏫",
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

        [Fact]
        public void ParseDraftReadsTheCommentAndMapsEveryRating()
        {
            var draft = SchoolAiService.ParseDraft(Answer);

            Assert.NotNull(draft);
            Assert.Equal("Strong teachers, weak labs 🏫", draft.Comment);
            Assert.Equal(4, draft.ClassesQualityRate);
            Assert.Equal(5, draft.EducationRate);
            Assert.Equal(2, draft.ITTrainingRate);
            Assert.Equal(4, draft.SafetyAndHappinessRate);
            Assert.Equal(3, draft.BehaviorRate);
            Assert.Equal(4, draft.TuitionRatioRate);
            Assert.Equal(3, draft.FacilitiesRate);
            Assert.Equal(2, draft.ArtisticActivitiesRate);
        }

        [Fact]
        public void ParseDraftKeepsRatingsBetweenOneAndFive()
        {
            var draft = SchoolAiService.ParseDraft("""{"description":"ok","ratings":{"classrooms_quality":9,"teachers_proficiency":0,"technology_access":"high"}}""");

            Assert.NotNull(draft);
            Assert.Equal(5, draft.ClassesQualityRate);
            Assert.Equal(1, draft.EducationRate);
            // Missing or not a number: the middle of the scale.
            Assert.Equal(3, draft.ITTrainingRate);
            Assert.Equal(3, draft.ArtisticActivitiesRate);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("""{"ratings":{}}""")]
        [InlineData("""{"description":"   ","ratings":{}}""")]
        [InlineData("""{"description":"ok"}""")]
        [InlineData("[]")]
        public void ParseDraftRejectsAnswersItCannotUse(string content) => Assert.Null(SchoolAiService.ParseDraft(content));

        [Fact]
        public void DraftPromptUsesTheSchoolsOwnData()
        {
            var prompt = SchoolAiService.BuildDraftPrompt(new SchoolDto
            {
                Name = "Alborz High School",
                CountryTitle = "Iran",
                StateTitle = "Tehran",
                CityTitle = "Tehran",
                WebSite = "https://alborz.example",
            });

            Assert.Contains("- **Name:** Alborz High School", prompt, StringComparison.Ordinal);
            Assert.Contains("- **Location:** Iran, Tehran, Tehran", prompt, StringComparison.Ordinal);
            Assert.Contains("- **Website:** https://alborz.example", prompt, StringComparison.Ordinal);
            Assert.Contains("primary language used in** `Iran`", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public void DraftPromptLeavesOutAMissingWebsite()
        {
            var prompt = SchoolAiService.BuildDraftPrompt(new SchoolDto { Name = "A", CountryTitle = "Italy" });

            Assert.DoesNotContain("Website", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public void PolishPromptEndsWithTheAdminComment()
        {
            var prompt = SchoolAiService.BuildPolishPrompt("ur photo is bad, rejected");

            Assert.StartsWith("You are a professional English writing editor", prompt, StringComparison.Ordinal);
            Assert.EndsWith("Admin comment:\nur photo is bad, rejected", prompt, StringComparison.Ordinal);
        }
    }
}
