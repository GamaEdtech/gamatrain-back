namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Raw shape (the fields this feature actually uses) of gama-api's GET /exams/{id} detail
    /// endpoint - side-effect-free, same role as GamaApiPaperDetailsResponse but for Exam. gama-api
    /// reports two independent prices here: `price.participation` (taking the exam - unrelated to
    /// this download feature, maps to the separate, not-yet-enforced FeatureCodes.ExamParticipation)
    /// and `price.pdf` (downloading it - the one this feature charges for). Only Pdf is mapped.
    /// </summary>
    public sealed class GamaApiExamDetailsResponse
    {
        [JsonPropertyName("price")]
        public GamaApiExamDetailsPrice? Price { get; set; }
    }

    public sealed class GamaApiExamDetailsPrice
    {
        [JsonPropertyName("pdf")]
        public GamaApiExamDetailsFileStatus? Pdf { get; set; }
    }

    public sealed class GamaApiExamDetailsFileStatus
    {
        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("paid")]
        public bool Paid { get; set; }
    }
}
