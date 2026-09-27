namespace GamaEdtech.Data.Dto.Game
{
    using System.Collections.Generic;

    using GamaEdtech.Domain.Enumeration;

    /// <summary>An exam's export prices for the current user, per paid format (see <see cref="ExamExportPricing"/>).</summary>
    public sealed class ExportPricesResponseDto
    {
        public int QuestionCount { get; set; }

        public IEnumerable<ItemDto>? Items { get; set; }

        public sealed class ItemDto
        {
            public required ExportFileType FileType { get; set; }

            /// <summary>What exporting this format costs: question count x its multiplier, rounded up.</summary>
            public long Points { get; set; }

            /// <summary>This user already bought this exam in this format -- exporting it again is free.</summary>
            public bool Purchased { get; set; }
        }
    }
}
