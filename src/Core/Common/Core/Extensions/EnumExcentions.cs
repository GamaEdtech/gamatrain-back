namespace GamaEdtech.Common.Core.Extensions
{
    using GamaEdtech.Common.Core.Extensions;

    using System.ComponentModel.DataAnnotations;
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;
    public static class EnumExtensions
    {
        public static IEnumerable<T> GetEnumValues<T>(this T input) where T : struct =>
            !typeof(T).IsEnum ? throw new NotSupportedException() : Enum.GetValues(input.GetType()).Cast<T>();

        public static IEnumerable<T> GetEnumFlags<T>([NotNull] this T input) where T : Enum
        {
            ValidateInput(input);
            return GetFlags(input);
        }

        private static void ValidateInput<T>(T input) where T : Enum
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input), "Input cannot be null.");
            }
        }

        private static IEnumerable<T> GetFlags<T>(T input) where T : Enum
            => from T value in Enum.GetValues(typeof(T))
               where input.HasFlag(value)
               select value;

        public static string ToDisplay([NotNull] this Enum value, DisplayProperty property = DisplayProperty.Name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value), "The enum value cannot be null.");
            }

            var fieldInfo = value.GetType().GetField(value.ToString());
            if (fieldInfo == null)
            {
                return value.ToString();
            }

            var attribute = fieldInfo.GetCustomAttributes<DisplayAttribute>(false).FirstOrDefault();
            if (attribute == null)
            {
                return value.ToString();
            }

            var propertyInfo = attribute.GetType().GetProperty(property.ToString());
            if (propertyInfo == null)
            {
                return value.ToString();
            }

            var propValue = propertyInfo.GetValue(attribute, null);
            return propValue?.ToString() ?? string.Empty;
        }

        public static Dictionary<int, string> ToDictionary([NotNull] this Enum value)
            => Enum.GetValues(value.GetType()).Cast<Enum>().ToDictionary(Convert.ToInt32, q => q.ToDisplay());
    }

    public enum DisplayProperty
    {
        Description,
        GroupName,
        Name,
        Prompt,
        ShortName,
        Order
    }
}
