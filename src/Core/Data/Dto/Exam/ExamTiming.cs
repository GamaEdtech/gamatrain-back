namespace GamaEdtech.Data.Dto.Exam
{
    using System.Globalization;

    /// <summary>
    /// How long a student took on an online exam. gama-api's <c>submit_time</c> is wall-clock seconds from start to submit: it
    /// is often 3.5 hours too large (its Tehran-time bug) and also counts the time the exam was closed, since the exam page keeps
    /// its countdown in the browser and resumes it later. So this backend records the countdown's own time spent when the exam
    /// is submitted through it, and shows gama-api's figure only when it is believable. See docs/business/exams-and-content.md,
    /// "Time spent on an exam".
    /// </summary>
    public static class ExamTiming
    {
        /// <summary>The 3.5 hours gama-api's <c>submit_time</c> is often too large by.</summary>
        public const int GamaApiOffsetSeconds = 12_600;

        /// <summary>Slack over the exam's duration for a slow submit.</summary>
        public const int GraceSeconds = 120;

        /// <summary>gama-api's <c>submit_time</c> when believable (within the duration, as it is or less the 3.5 hours); else null.</summary>
        public static int? FromGamaApi(int? seconds, int? durationMinutes)
        {
            if (seconds is not > 0 || durationMinutes is not > 0)
            {
                return null;
            }

            var limit = (durationMinutes.Value * 60) + GraceSeconds;
            if (seconds <= limit)
            {
                return seconds;
            }

            var corrected = seconds.Value - GamaApiOffsetSeconds;
            return corrected is > 0 && corrected <= limit ? corrected : null;
        }

        /// <summary>The browser countdown's time spent, never negative and never more than the exam's duration.</summary>
        public static int? FromStudent(int? seconds, int? durationMinutes) => seconds switch
        {
            null or < 0 => null,
            _ when durationMinutes is > 0 => Math.Min(seconds.Value, durationMinutes.Value * 60),
            _ => seconds,
        };

        /// <summary>gama-api sends numbers as text ("60"); null when it isn't a whole number.</summary>
        public static int? ParseWhole(string? text) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
