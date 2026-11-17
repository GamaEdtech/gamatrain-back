namespace GamaEdtech.Application.Service
{
    using System.Collections.Frozen;

    /// <summary>
    /// Which subjects' topics are matched against which: gama-api's <c>gamatrain_lessons_correlation</c>, copied as it is
    /// (2026-11-17). Each GAMA subject (the key) corresponds to the listed subjects of other boards. Matching is always
    /// between GAMA and one other board, never between two other boards: a GAMA subject's candidates are its boards'
    /// subjects, another board's subject's candidates are the GAMA subjects that list it. See docs/business/tutorials.md,
    /// "Topic matching".
    /// </summary>
    internal static class GamaSubjects
    {
        private static readonly int[] Biology =
        [
            2204, // Cambridge IGCSE - Biology
            6403, // Cambridge O-Level - Biology
            4164, // Cambridge AS&A Level - Biology
            6378, // Cambridge 9-1 IGCSE - Biology
            6842, // Cambridge Pre-U - Biology
            6948, // Edexcel IGCSE - Biology
            6961, // Edexcel IGCSE - Human Biology
            7134, // Edexcel GCSE - Biology
            7110, // Edexcel IAS/IAL - Biology
            7063, // Edexcel A Level - Biology A
            7064, // Edexcel A Level - Biology B
            7020, // Edexcel AS Level - Biology A
            7021, // Edexcel AS Level - Biology B
            6717, // AQA GCSE - Biology
            6682, // AQA AS Level - Biology
            6750, // AQA A Level - Biology
            6822, // OCR GCSE - Biology A
            6823, // OCR GCSE - Biology B
            7777, // OCR AS Level - Biology A
            7778, // OCR AS Level - Biology B
            7744, // OCR A Level - Biology A
            7745, // OCR A Level - Biology B
            8690, // CXC CSEC - Biology
            9274, // Nigerian JAMB - Biology
        ];

        private static readonly int[] Chemistry =
        [
            2203, // Cambridge IGCSE - Chemistry
            6405, // Cambridge O-Level - Chemistry
            4166, // Cambridge AS&A Level - Chemistry
            6380, // Cambridge 9-1 IGCSE - Chemistry
            6844, // Cambridge Pre-U - Chemistry
            6949, // Edexcel IGCSE - Chemistry
            7136, // Edexcel GCSE - Chemistry
            7111, // Edexcel IAS/IAL - Chemistry
            7065, // Edexcel A Level - Chemistry
            7022, // Edexcel AS Level - Chemistry
            6719, // AQA GCSE - Chemistry
            6683, // AQA AS Level - Chemistry
            6752, // AQA A Level - Chemistry
            6825, // OCR GCSE - Chemistry A
            6826, // OCR GCSE - Chemistry B
            7780, // OCR AS Level - Chemistry A
            7781, // OCR AS Level - Chemistry B
            7747, // OCR A Level - Chemistry A
            7748, // OCR A Level - Chemistry B
            8697, // CXC CSEC - Chemistry
            9272, // Nigerian JAMB - Chemistry
        ];

        private static readonly int[] Physics =
        [
            2195, // Cambridge IGCSE - Physics
            6426, // Cambridge O-Level - Physics
            4194, // Cambridge AS&A Level - Physics
            6596, // Cambridge 9-1 IGCSE - Physics
            6869, // Cambridge Pre-U - Physics
            6950, // Edexcel IGCSE - Physics
            7139, // Edexcel GCSE - Physics
            7112, // Edexcel IAS/IAL - Physics
            7066, // Edexcel A Level - Physics
            7023, // Edexcel AS Level - Physics
            6741, // AQA GCSE - Physics
            6695, // AQA AS Level - Physics
            6764, // AQA A Level - Physics
            6911, // OCR GCSE - Physics A
            6912, // OCR GCSE - Physics B
            7803, // OCR AS Level - Physics A
            7804, // OCR AS Level - Physics B
            7770, // OCR A Level - Physics A
            7771, // OCR A Level - Physics B
            8686, // CXC CSEC - Physics
            9273, // Nigerian JAMB - Physics
        ];

        private static readonly int[] Geology =
        [
            7794, // OCR AS Level - Geology
            7761, // OCR A Level - Geology
        ];

        private static readonly int[] MathGeneral =
        [
            2194, // Cambridge IGCSE - Mathematics
            6423, // Cambridge O-Level - Additional Mathematics
            6424, // Cambridge O-Level - Mathematics Syllabus D
            4190, // Cambridge AS&A Level - Mathematics
            6593, // Cambridge 9-1 IGCSE - Mathematics
            6865, // Cambridge Pre-U - Mathematics
            6866, // Cambridge Pre-U - Mathematics
            6946, // Edexcel IGCSE - Mathematics A
            6947, // Edexcel IGCSE - Mathematics B
            7138, // Edexcel GCSE - Mathematics
            7105, // Edexcel IAS/IAL - Pure Mathematics
            7062, // Edexcel A Level - Mathematics
            7019, // Edexcel AS Level - Mathematics
            6737, // AQA GCSE - Math
            6694, // AQA AS Level - Mathematics
            6763, // AQA A Level - Mathematics
            6907, // OCR GCSE - Mathematics
            7798, // OCR AS Level - Mathematics A
            7799, // OCR AS Level - Mathematics B
            7765, // OCR A Level - Mathematics A
            7766, // OCR A Level - Mathematics B
            8677, // CXC CSEC - Mathematics
            9271, // Nigerian JAMB - Mathematics
        ];

        private static readonly int[] MathFurther =
        [
            4179, // Cambridge AS&A Level - Mathematics Further
            6851, // Cambridge Pre-U - Further Mathematics
            6852, // Cambridge Pre-U - Further Mathematics
            6957, // Edexcel IGCSE - Further Pure Mathematics
            7108, // Edexcel IAS/IAL - Further Math
            7070, // Edexcel A Level - Further Mathematics
            7027, // Edexcel AS Level - Further Mathematics
            6731, // AQA GCSE - Further Maths
            6690, // AQA AS Level - Further Mathematics
            6759, // AQA A Level - Further Maths
            7791, // OCR AS Level - Further Mathematics A
            7792, // OCR AS Level - Further Mathematics B
            7758, // OCR A Level - Further Mathematics A
            7759, // OCR A Level - Further Mathematics B
        ];

        private static readonly int[] MathStats =
        [
            6432, // Cambridge O-Level - Statistics
            7684, // Edexcel GCSE - Statistics
            7107, // Edexcel IAS/IAL - Statistics
            7072, // Edexcel A Level - Statistics
            7029, // Edexcel AS Level - Statistics
            6748, // AQA GCSE - Statistics
        ];

        private static readonly int[] MathDecision =
        [
            7109, // Edexcel IAS/IAL - Decision Mathematics
        ];

        private static readonly FrozenDictionary<int, int[]> Correlation = new Dictionary<int, int[]>
        {
            // High School (7132)
            [10046] = Biology, // Biology 1
            [10047] = Biology, // Biology 2
            [10048] = Biology, // Biology 3
            [9693] = Chemistry, // Chemistry 1
            [9694] = Chemistry, // Chemistry 2
            [9695] = Chemistry, // Chemistry 3
            [9766] = Physics, // General & Thermal Physics
            [9767] = Physics, // Mechanics in Physics
            [9768] = Physics, // Electricity and Magnetism
            [9769] = Physics, // Waves, Quantum and Nuclear Physics
            [7133] = Physics, // Physics
            [7520] = Physics, // Physics Lab
            [9875] = Geology, // Geology
            [9877] = MathGeneral, // Algebra and Equations
            [9879] = MathGeneral, // Trigonometry
            [9880] = MathGeneral, // Geometry Foundations
            [9881] = MathGeneral, // Coordinate and Analytic Geometry
            [9878] = [.. MathGeneral, .. MathFurther], // Calculus
            [10190] = [.. MathGeneral, .. MathFurther, .. MathDecision], // Discrete Mathematics
            [9876] = [.. MathGeneral, .. MathStats], // Statistics and Probability
            // Middle School (7643)
            [7895] = [3476, 3477, 3479, 6713], // Physics -> Cambridge Science 7/8/9, Edexcel Science
            [7896] = [3476, 3477, 3479, 6713], // Chemistry -> Cambridge Science 7/8/9, Edexcel Science
            [7897] = [3476, 3477, 3479, 6713], // Biology -> Cambridge Science 7/8/9, Edexcel Science
            [7898] = [3476, 3477, 3479, 6713], // Earth Systems & Environment -> Cambridge Science 7/8/9, Edexcel Science
            [8119] = [3472, 3474, 3475, 6712], // Numbers & Calculation -> Cambridge Math 7/8/9, Edexcel Mathematic
            [8120] = [3472, 3474, 3475, 6712], // Algebra -> Cambridge Math 7/8/9, Edexcel Mathematic
            [8121] = [3472, 3474, 3475, 6712], // Geometry & Measurement -> Cambridge Math 7/8/9, Edexcel Mathematic
            [8122] = [3472, 3474, 3475, 6712], // Data & Probability -> Cambridge Math 7/8/9, Edexcel Mathematic
            // Primary School (7641)
            [8639] = [1129, 6710], // Primary Science 1 -> Cambridge Science 1, Edexcel Science
            [8640] = [1133, 6710], // Primary Science 2 -> Cambridge Science 2, Edexcel Science
            [8641] = [1137, 6710], // Primary Science 3 -> Cambridge Science 3, Edexcel Science
            [8642] = [1141, 6710], // Primary Science 4 -> Cambridge Science 4, Edexcel Science
            [8638] = [1145, 6710], // Primary Science 5 -> Cambridge Science 5, Edexcel Science
            [8643] = [1149, 6710], // Primary Science 6 -> Cambridge Science 6, Edexcel Science
            [9506] = [1128, 6709], // Primary Math 1 -> Cambridge Mathematics 1, Edexcel Mathematic
            [9537] = [1132, 6709], // Primary Math 2 -> Cambridge Mathematics 2, Edexcel Mathematic
            [9454] = [1136, 6709], // Primary Math 3 -> Cambridge Mathematics 3, Edexcel Mathematic
            [9401] = [1140, 6709], // Primary Math 4 -> Cambridge Mathematics 4, Edexcel Mathematic
            [9345] = [1144, 6709], // Primary Math 5 -> Cambridge Mathematics 5, Edexcel Mathematic
            [9571] = [1148, 6709], // Primary Math 6 -> Cambridge Mathematics 6, Edexcel Mathematic
        }.ToFrozenDictionary();

        private static readonly FrozenDictionary<int, int[]> Related = BuildRelated();

        /// <summary>Whether the subject is a GAMA subject (a key of the correspondence).</summary>
        internal static bool IsGama(int subjectId) => Correlation.ContainsKey(subjectId);

        /// <summary>The subjects whose topics a topic of this subject is compared with (gama-api's
        /// <c>getGamatrainCandidateLessons</c>): the other boards' for a GAMA subject, the GAMA ones for another board's.</summary>
        internal static IReadOnlyList<int> CandidateSubjects(int subjectId) =>
            [.. (Related.GetValueOrDefault(subjectId) ?? []).Where(t => IsGama(t) != IsGama(subjectId))];

        /// <summary>gama-api's reverse map: every subject in a GAMA subject's group is related to every other one in it.</summary>
        private static FrozenDictionary<int, int[]> BuildRelated()
        {
            Dictionary<int, List<int>> related = [];
            foreach (var (gama, others) in Correlation)
            {
                int[] group = [gama, .. others];
                foreach (var id in group)
                {
                    if (!related.TryGetValue(id, out var list))
                    {
                        related[id] = list = [];
                    }

                    list.AddRange(group.Where(t => t != id && !list.Contains(t)));
                }
            }

            return related.ToFrozenDictionary(t => t.Key, t => t.Value.ToArray());
        }
    }
}
