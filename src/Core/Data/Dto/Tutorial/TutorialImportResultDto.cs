namespace GamaEdtech.Data.Dto.Tutorial
{
    /// <summary>What one run of the tutorial copy did.</summary>
    public sealed class TutorialImportResultDto
    {
        public int TutorialsRead { get; set; }

        public int TutorialsAdded { get; set; }

        public int TutorialsUpdated { get; set; }

        /// <summary>Tutorials whose gama-api owner is not linked to a user here (yet).</summary>
        public int TutorialsWithoutUser { get; set; }

        public int ImagesRead { get; set; }

        public int ImagesAdded { get; set; }

        public int ImagesUpdated { get; set; }

        /// <summary>Image rows whose tutorial was not copied, or that name no file; skipped.</summary>
        public int ImagesSkipped { get; set; }
    }
}
