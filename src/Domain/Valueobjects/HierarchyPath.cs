namespace GamaEdtech.Domain.Valueobjects
{
    public record HierarchyPath(string Value)
    {
        public static readonly char ParentPathSeparator = '|';
        public static HierarchyPath Root() => new("/");
        public static HierarchyPath FromString(string value) => new(value);
        public IEnumerable<string> GetPaths() => Value.Split(ParentPathSeparator);
        public int GetLevel() => GetPaths().First().Count(c => c == '/') - 1;

        public HierarchyPath GetDescendant(string defaultSegment, HierarchyPath? lastChild = null)
        {
            var paths = GetPaths().Select(path =>
            {
                string newSegment;
                if (lastChild == null)
                {
                    newSegment = defaultSegment;
                }
                else
                {
                    var trimmed = lastChild.Value.TrimEnd('/');
                    var segments = trimmed.Split('/');
                    var lastSegment = segments[^1];

                    newSegment = int.TryParse(lastSegment, out var lastNumber)
                        ? (lastNumber + 1).ToString()
                        : throw new InvalidOperationException("بخش انتهایی مسیر نامعتبر است.");
                }
                return $"{path}{newSegment}/";
            });

            return new HierarchyPath(string.Join(ParentPathSeparator, paths));
        }

        public override string ToString() => Value;
    }
}
