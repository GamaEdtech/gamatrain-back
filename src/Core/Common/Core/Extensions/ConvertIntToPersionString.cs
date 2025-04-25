namespace GamaEdtech.Common.Core.Extensions
{
    using System.Text;

    public static class ConvertIntToPersianString
    {
        public static string ConvertToPersianString(this int number)
        {
            if (number == 0)
            {
                return "صفر";
            }

            var result = new StringBuilder();
            var separator = string.Empty;
            var parts = SplitNumberToParts(number.ToString());

            var partIndex = 0;
            foreach (var part in parts.Reverse())
            {
                if (result.Length > 0)
                {
                    separator = " و ";
                }

                _ = result.Insert(0, ConvertPartToPersianString(part, partIndex) + separator);
                partIndex++;
            }

            return result.ToString();
        }

        private static string[] SplitNumberToParts(string numberString)
        {
            numberString = PadNumberString(numberString);
            var parts = new string[(numberString.Length / 3) + (numberString.Length % 3 == 0 ? 0 : 1)];
            var partIndex = 0;

            for (var i = 0; i < numberString.Length; i += 3)
            {
                parts[partIndex] = numberString.Substring(i, Math.Min(3, numberString.Length - i));
                partIndex++;
            }

            return parts;
        }

        private static string PadNumberString(string numberString)
        {
            var paddingLength = (3 - (numberString.Length % 3)) % 3;
            return new string('0', paddingLength) + numberString;
        }

        private static string ConvertPartToPersianString(string part, int partIndex)
        {
            var result = partIndex switch
            {
                0 => ConvertThreeDigitPartToPersian(part),
                1 => ConvertThreeDigitPartToPersian(part) + " هزار",
                2 => ConvertThreeDigitPartToPersian(part) + " میلیون",
                3 => ConvertThreeDigitPartToPersian(part) + " میلیارد",
                4 => ConvertThreeDigitPartToPersian(part) + " تیلیارد",
                5 => ConvertThreeDigitPartToPersian(part) + " بیلیارد",
                _ => ConvertThreeDigitPartToPersian(part) + partIndex.ToString(),
            };
            return result;
        }

        private static string ConvertThreeDigitPartToPersian(string part)
        {
            var result = string.Empty;

            var hundred = Convert.ToInt32(part[..1]);
            var ten = Convert.ToInt32(part.Substring(1, 1));
            var unit = Convert.ToInt32(part.Substring(2, 1));

            // Handle the hundreds place
            if (hundred > 0)
            {
                result += GetNumberInPersian(3, hundred, " ");
            }

            // Handle the tens and ones place
            if (ten > 0 || unit > 0)
            {
                result += GetNumberInPersian(2, ten, " ") + GetNumberInPersian(0, unit, " ");
            }

            return result;
        }

        private static string GetNumberInPersian(int place, int number, string separator)
        {
            var result = string.Empty;

            if (number == 0)
            {
                return result;
            }

            switch (place)
            {
                case 0: // Units
                    result = GetUnitsInPersian(number, separator);
                    break;
                case 1: // Tens
                    result = GetTensInPersian(number, separator);
                    break;
                case 2: // Hundreds
                    result = GetHundredsInPersian(number, separator);
                    break;
            }

            return result;
        }

        private static string GetUnitsInPersian(int number, string separator)
        {
            string[] units = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];
            string[] specialUnits = ["", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];

            return number is >= 11 and <= 19 ? specialUnits[number - 10] + separator : units[number] + separator;
        }

        private static string GetTensInPersian(int number, string separator)
        {
            string[] tens = ["", "ده", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
            return tens[number] + separator;
        }

        private static string GetHundredsInPersian(int number, string separator)
        {
            string[] hundreds = ["", "یکصد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
            return hundreds[number] + separator;
        }
    }
}
