namespace GamaEdtech.Common.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Dynamic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Net.Http;
    using System.Reflection;
    using System.Resources;
    using System.Runtime.CompilerServices;
    using System.Security.Claims;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Common.Core.Extensions.Collections;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Logging;

    using NUlid;

    using Slugify;

    public static partial class Globals
    {
        public static CultureInfo CurrentCulture => Thread.CurrentThread.CurrentUICulture;

        public static bool IsRtl => CurrentCulture.TextInfo.IsRightToLeft;

        public static DbProviderType ProviderType { get; set; }

        public static IEnumerable<CultureInfo> AllCultures => CultureInfo.GetCultures(CultureTypes.AllCultures).Where(t => !string.IsNullOrEmpty(t.Name)).Select(GetCulture);

        public static string? GetClientIpAddress(this HttpContext? httpContext)
        {
            var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();
            var xForwardedFor = httpContext?.Request.Headers["X-Forwarded-For"];
            if (xForwardedFor.HasValue)
            {
                ip = xForwardedFor.Value.ToString().Split(',')[^1];
            }

            return ip?.AsSpan().Contains('.') == true && ip.AsSpan().Contains(':') ? ip.AsSpan(0, ip.AsSpan().IndexOf(':')).ToString() : ip;
        }

        public static bool ValidateNationalCode(string? nationalCode)
        {
            try
            {
                var allDigitEqual = new[] { "0000000000", "1111111111", "2222222222", "3333333333", "4444444444", "5555555555", "6666666666", "7777777777", "8888888888", "9999999999" };

                if (string.IsNullOrEmpty(nationalCode) || allDigitEqual.Contains(nationalCode) || nationalCode.Length != 10)
                {
                    return false;
                }

                var chArray = nationalCode.ToCharArray();
                var num0 = Convert.ToInt32(chArray[0].ToString()) * 10;
                var num2 = Convert.ToInt32(chArray[1].ToString()) * 9;
                var num3 = Convert.ToInt32(chArray[2].ToString()) * 8;
                var num4 = Convert.ToInt32(chArray[3].ToString()) * 7;
                var num5 = Convert.ToInt32(chArray[4].ToString()) * 6;
                var num6 = Convert.ToInt32(chArray[5].ToString()) * 5;
                var num7 = Convert.ToInt32(chArray[6].ToString()) * 4;
                var num8 = Convert.ToInt32(chArray[7].ToString()) * 3;
                var num9 = Convert.ToInt32(chArray[8].ToString()) * 2;
                var a = Convert.ToInt32(chArray[9].ToString());

                var b = num0 + num2 + num3 + num4 + num5 + num6 + num7 + num8 + num9;
                var c = b % 11;

                return (c < 2 && a == c) || (c >= 2 && 11 - c == a);
            }
            catch
            {
                return false;
            }
        }

        public static bool ValidateNationalId(string? nationalId)
        {
            try
            {
                if (string.IsNullOrEmpty(nationalId))
                {
                    return false;
                }

                const int initialZeros = 3;
                const int nationalIdLength = 11;

                if (nationalId.Length is < nationalIdLength - initialZeros or > nationalIdLength)
                {
                    return false;
                }

                nationalId = nationalId.PadLeft(11, '0');

                if (!nationalId.All(char.IsDigit))
                {
                    return false;
                }

                var beforeControlNumber = (int)char.GetNumericValue(nationalId[9]) + 2;
                int[] coefficientStatic = [29, 27, 23, 19, 17, 29, 27, 23, 19, 17];

                var sum = 0;
                for (var i = 0; i < nationalId.Length - 1; i++)
                {
                    sum += ((int)char.GetNumericValue(nationalId[i]) + beforeControlNumber) * coefficientStatic[i];
                }

                var remainder = sum % 11;
                var controlNumber = (int)char.GetNumericValue(nationalId[10]);
                remainder = remainder == 10 ? 0 : remainder;

                return controlNumber == remainder;
            }
            catch
            {
                return false;
            }
        }

        public static string? GetLocalizedDisplayName(MemberInfo? member)
        {
            if (member is null)
            {
                return null;
            }

            string? name;
            var displayAttribute = member.GetCustomAttribute<DisplayAttribute>(false);
            if (displayAttribute is not null)
            {
                name = GetLocalizedValueInternal(displayAttribute, member.Name, Constants.ResourceKey.Name, member: member);
                return name.IsNullOrEmpty() ? member.Name : name;
            }

            var customAttribute = member.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>(false);
            name = customAttribute?.GetName();
            return string.IsNullOrEmpty(name) ? member.Name : name;
        }

        public static string? DisplayNameFor<T>([NotNull] this Expression<Func<T, object>> expression)
            where T : class
        {
            var memberExpression = expression.Body as MemberExpression;
            if (memberExpression?.Member.MemberType == MemberTypes.Property)
            {
                return GetLocalizedDisplayName(memberExpression.Member);
            }

            if (expression.Body is UnaryExpression unaryExpression)
            {
                memberExpression = unaryExpression.Operand as MemberExpression;
                if (memberExpression?.Member.MemberType == MemberTypes.Property)
                {
                    return GetLocalizedDisplayName(memberExpression.Member);
                }
            }

            return string.Empty;
        }

        public static string? GetLocalizedShortName(MemberInfo? member)
        {
            if (member is null)
            {
                return null;
            }

            var customDisplay = member.GetCustomAttribute<DisplayAttribute>(false);
            if (customDisplay is not null)
            {
                return GetLocalizedValueInternal(customDisplay, member.Name, Constants.ResourceKey.ShortName, member: member);
            }

            var customAttribute = member.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>(false);
            return customAttribute?.GetShortName();
        }

        public static string? GetLocalizedDescription(MemberInfo? member)
        {
            if (member is null)
            {
                return null;
            }

            var customDisplay = member.GetCustomAttribute<DisplayAttribute>(false);
            if (customDisplay is not null)
            {
                return GetLocalizedValueInternal(customDisplay, member.Name, Constants.ResourceKey.Description, member: member);
            }

            var customAttribute = member.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>(false);
            return customAttribute?.GetDescription();
        }

        public static string? GetLocalizedPromt(MemberInfo? member)
        {
            if (member is null)
            {
                return null;
            }

            var customDisplay = member.GetCustomAttribute<DisplayAttribute>(false);
            if (customDisplay is not null)
            {
                return GetLocalizedValueInternal(customDisplay, member.Name, Constants.ResourceKey.Prompt, member: member);
            }

            var customAttribute = member.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>(false);
            return customAttribute?.GetPrompt();
        }

        public static string? GetLocalizedGroupName(MemberInfo? member)
        {
            if (member is null)
            {
                return null;
            }

            var customDisplay = member.GetCustomAttribute<DisplayAttribute>(false);
            if (customDisplay is not null)
            {
                return GetLocalizedValueInternal(customDisplay, member.Name, Constants.ResourceKey.GroupName, member: member);
            }

            var customAttribute = member.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>(false);
            return customAttribute?.GetGroupName();
        }

        public static T? ValueOf<T>([NotNull] this Dictionary<string, string> dictionary, string key, T? defaultValue = default)
        {
            _ = dictionary.TryGetValue(key, out var tmp);
            return tmp.ValueOf(defaultValue);
        }

        public static T? ValueOf<T>(this string? value, T? defaultValue = default) => value.ValueOf(typeof(T), defaultValue);

        public static dynamic? ValueOf(this string? value, [NotNull] Type type, dynamic? defaultValue)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                {
                    return defaultValue;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    type = Nullable.GetUnderlyingType(type) ?? type;
                }

                if (type.IsEnum)
                {
                    try
                    {
                        return Enum.Parse(type, value);
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }

                var typeCode = Type.GetTypeCode(type);

                switch (typeCode)
                {
                    case TypeCode.Boolean:
                        return bool.TryParse(value, out var boolTmp) ? boolTmp : defaultValue;

                    case TypeCode.SByte:
                    case TypeCode.Byte:
                        return byte.TryParse(value, out var byteTmp) ? byteTmp : defaultValue;

                    case TypeCode.Int16:
                    case TypeCode.UInt16:
                        return short.TryParse(value, out var shortTmp) ? shortTmp : defaultValue;

                    case TypeCode.Int32:
                    case TypeCode.UInt32:
                        return int.TryParse(value, out var intTmp) ? intTmp : defaultValue;

                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                        return long.TryParse(value, out var longTmp) ? longTmp : defaultValue;

                    case TypeCode.Single:
                        return float.TryParse(value, out var floatTmp) ? floatTmp : defaultValue;

                    case TypeCode.Double:
                        return double.TryParse(value, out var doubleTmp) ? doubleTmp : defaultValue;

                    case TypeCode.Decimal:
                        return decimal.TryParse(value, out var decimalTmp) ? decimalTmp : defaultValue;

                    case TypeCode.DateTime:
                    {
                        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeTmp) ? dateTimeTmp : defaultValue;
                    }

                    case TypeCode.String:
                    case TypeCode.Char:
                        return value;

                    case TypeCode.Object:
                        if (type.Name == nameof(Ulid))
                        {
                            return Ulid.TryParse(value, out var ulid) ? ulid : defaultValue;
                        }

                        if (type.Name == nameof(DateTimeOffset))
                        {
                            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffsetTmp) ? dateTimeOffsetTmp : defaultValue;
                        }

                        if (type.Name == nameof(TimeSpan))
                        {
                            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpanTmp) ? timeSpanTmp : defaultValue;
                        }

                        if (type.Name == nameof(Guid))
                        {
                            return Guid.TryParse(value, out var guidTmp) ? guidTmp : defaultValue;
                        }

                        if (type.Name == nameof(TimeOnly))
                        {
                            if (TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var timeOnlyTmp))
                            {
                                return timeOnlyTmp;
                            }

                            var parse = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeTmp);
                            return parse ? TimeOnly.FromDateTime(dateTimeTmp) : defaultValue;
                        }

                        if (type.Name == nameof(DateOnly))
                        {
                            if (TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var dateOnlyTmp))
                            {
                                return dateOnlyTmp;
                            }

                            var parse = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeTmp);
                            return parse ? DateOnly.FromDateTime(dateTimeTmp) : defaultValue;
                        }

                        throw new ArgumentException(typeCode.ToString());

                    default:
                        throw new ArgumentException(typeCode.ToString());
                }
            }
            catch
            {
                throw new ArgumentException(value);
            }
        }

        public static string? UserAgent(this HttpContext? httpContext) => httpContext?.Request.Headers.UserAgent.ToString();

        public static long UserId(this HttpContext? httpContext) => httpContext.UserId<long>();

        public static T? UserId<T>(this HttpContext? httpContext)
            => (httpContext?.User).UserId<T>();

        public static long UserId(this ClaimsPrincipal? claimsPrincipal) => claimsPrincipal.UserId<long>();

        public static T? UserId<T>(this ClaimsPrincipal? claimsPrincipal)
            => claimsPrincipal is null ? default : claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier).ValueOf<T>();

        /// <summary>The account email, lower-cased, only when the account has confirmed it; otherwise null. Use this, not the raw
        /// email claim, wherever the email is taken as proof of who the caller is (public sign-up does not confirm it).</summary>
        public static string? ConfirmedEmail(this ClaimsPrincipal? claimsPrincipal)
        {
            if (claimsPrincipal?.HasClaim(Constants.EmailConfirmedClaim, bool.TrueString) != true)
            {
                return null;
            }

            var email = claimsPrincipal.FindFirstValue(ClaimTypes.Email);
            return string.IsNullOrEmpty(email) ? null : email.ToLowerInvariant();
        }

        public static IDictionary<string, object?>? ObjectToDictionary(object value)
        {
            if (value is IDictionary<string, object?> dictionary)
            {
                return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
            }

            dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (value is not null)
            {
                dictionary = value.GetType().GetProperties().ToDictionary(t => t.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? t.Name, t => t.GetValue(value, null));
            }

            return dictionary;
        }

        public static CultureInfo GetCulture([NotNull] CultureInfo culture)
        {
            if (!culture.Name.StartsWith("fa", StringComparison.InvariantCultureIgnoreCase))
            {
                return culture;
            }

            var persianCalture = new CultureInfo(culture.Name, false);
            var info = persianCalture.DateTimeFormat;
            var monthNames = new[] { "فروردين", "ارديبهشت", "خرداد", "تير", "مرداد", "شهريور", "مهر", "آبان", "آذر", "دي", "بهمن", "اسفند", string.Empty };
            var shortestDayNames = new[] { "ى", "د", "س", "چ", "پ", "ج", "ش" };
            var dayNames = new[] { "يکشنبه", "دوشنبه", "سه شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه" };

            info.MonthGenitiveNames = monthNames;
            info.AbbreviatedMonthGenitiveNames = monthNames;
            info.AbbreviatedMonthNames = monthNames;
            info.MonthNames = monthNames;

            info.ShortestDayNames = shortestDayNames;
            info.AbbreviatedDayNames = shortestDayNames;

            info.DayNames = dayNames;

            info.DateSeparator = "/";
            info.FullDateTimePattern = "dddd dd MMM yyyy HH:mm:ss";
            info.LongDatePattern = "dddd dd MMM yyyy";
            info.LongTimePattern = "HH:mm:ss";
            info.MonthDayPattern = "dd MMM";
            info.ShortTimePattern = "HH:mm";
            info.TimeSeparator = ":";
            info.YearMonthPattern = "MMM yyyy";
            info.AMDesignator = "ق.ظ";
            info.PMDesignator = "ب.ظ";
            info.ShortDatePattern = "yyyy/MM/dd";
            info.FirstDayOfWeek = System.DayOfWeek.Saturday;
            persianCalture.DateTimeFormat = info;

            persianCalture.NumberFormat.NumberDecimalDigits = 0;
            persianCalture.NumberFormat.CurrencyDecimalDigits = 0;
            persianCalture.NumberFormat.PercentDecimalDigits = 0;
            persianCalture.NumberFormat.CurrencyPositivePattern = 1;
            persianCalture.NumberFormat.NumberDecimalSeparator = ".";

            var persianCal = new MyPersianCalendar();

            var fieldInfo = typeof(DateTimeFormatInfo).GetField("calendar", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            fieldInfo?.SetValue(info, persianCal);

            var field = typeof(CultureInfo).GetField("calendar", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(persianCalture, persianCal);

            return persianCalture;
        }

        public static IEnumerable<Type>? GetAllTypesImplementingType([NotNull] this Type mainType, IEnumerable<Type> scanTypes)
        {
            IEnumerable<Type>? types = null;
            if (mainType.IsGenericType)
            {
                types = (from t1 in scanTypes
                         from t2 in t1.GetInterfaces()
                         let baseType = t1.BaseType
                         where !t1.IsAbstract &&
                         ((baseType is not null && baseType.IsGenericType && mainType.IsAssignableFrom(baseType.GetGenericTypeDefinition())) ||
                         (t2.IsGenericType && mainType.IsAssignableFrom(t2.GetGenericTypeDefinition())))
                         select t1);
            }

            types ??= mainType.IsInterface
                        ? scanTypes.Where(t => !t.IsAbstract && t.GetInterfaces().Contains(mainType))
                        : scanTypes.Where(t => !t.IsAbstract && t.IsSubclassOf(mainType));

            if (!types?.Any() == true && mainType.IsClass && !mainType.IsAbstract)
            {
                types = new[] { mainType };
            }

            return types?.Distinct();
        }

        public static bool IsSubclassOf(Type? type, Type? baseType)
        {
            while (type != null && type != typeof(object))
            {
                var cur = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                if (baseType == cur)
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        public static string TrimEnd([NotNull] this string input, string? suffixToRemove, StringComparison comparisonType = StringComparison.CurrentCulture) => suffixToRemove is not null && input.EndsWith(suffixToRemove, comparisonType) ? input[..^suffixToRemove.Length] : input;

        public static string? Slugify(this string? value) => value is null ? null : new SlugHelper().GenerateSlug(value);

        public static async Task<string?> ConvertImageToBase64Async(this IFormFile? file)
        {
            if (file is null)
            {
                return null;
            }

            using var target = new MemoryStream();
            await file.CopyToAsync(target);
            return $"data:{file.ContentType};base64, {Convert.ToBase64String(target.ToArray())}";
        }

        public static bool IsImage(string? fileName) => !fileName.IsNullOrEmpty() && !fileName.StartsWith("<svg", StringComparison.InvariantCultureIgnoreCase) && (fileName?.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) == true || Constants.ValidImageExtensions.Contains(Path.GetExtension(fileName)?.TrimStart('.') ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        public static bool ValidateIban(this string? iban)
        {
            if (string.IsNullOrEmpty(iban))
            {
                return true;
            }

            if (iban.Length != 26 || !iban[..2].ToCharArray().All(char.IsLetter))
            {
                return false;
            }

            iban = iban.ToUpperInvariant();
            var changediban = iban.Substring(4, 22);
            changediban = changediban.Insert(22, (Convert.ToInt16(iban[0]) - 55).ToString());
            changediban = changediban.Insert(24, (Convert.ToInt16(iban[1]) - 55).ToString());
            changediban = changediban.Insert(26, iban.Substring(2, 2));
            return decimal.Parse(changediban) % 97 == 1;
        }

        public static HttpClient CreateHttpClient([NotNull] this IHttpClientFactory httpClientFactory, bool forceTls13 = false) => httpClientFactory.CreateClient(forceTls13 ? Constants.HttpClientIgnoreSslAndAutoRedirectTls13 : Constants.HttpClientIgnoreSslAndAutoRedirect);

        public static dynamic GetAllResourceString(string defaultNamespace)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()?.FirstOrDefault(t => t.FullName?.Contains($"{defaultNamespace}.Resource", StringComparison.OrdinalIgnoreCase) == true);
            if (assembly == null)
            {
                return string.Empty;
            }

            var names = assembly.GetManifestResourceNames();
            string[] validPrefixes =
            [
                $"{defaultNamespace}.Resource.UI.Web.Api.",
                    $"{defaultNamespace}.Resource.Data.ViewModel.",
                    $"{defaultNamespace}.Resource.Data.Enumeration.",
                ];
            dynamic expando = new ExpandoObject();
            Dictionary<string, object?> dataList = [];
            for (var i = 0; i < names.Length; i++)
            {
                var item = names[i];
                if (!validPrefixes.Exists(item.StartsWith))
                {
                    continue;
                }

                using var cultureResourceStream = assembly.GetManifestResourceStream(item);
                if (cultureResourceStream is null)
                {
                    continue;
                }

                var baseName = item.TrimEnd(".resources");
                var manager = new ResourceManager(baseName, assembly);
                using var resources = new ResourceReader(cultureResourceStream);
                Dictionary<string, object?> nestedList = [];
                foreach (DictionaryEntry entry in resources)
                {
                    var key = (string)entry.Key;
                    var text = manager.GetString(key, CultureInfo.CurrentCulture);

                    var lst = key.Replace("_Name", string.Empty, StringComparison.OrdinalIgnoreCase).Split("_", 2, StringSplitOptions.RemoveEmptyEntries);
                    if (lst.Length == 1)
                    {
                        nestedList.Add(lst[0], text);
                    }
                    else
                    {
                        if (nestedList.TryGetValue(lst[0], out var value))
                        {
                            _ = (value as Dictionary<string, object?>)?.TryAdd(lst[1], text);
                        }
                        else
                        {
                            nestedList.Add(lst[0], new Dictionary<string, object?> { { lst[1], text } });
                        }
                    }
                }

                dataList.Add(baseName.Replace($"{defaultNamespace}.Resource.", string.Empty, StringComparison.OrdinalIgnoreCase), nestedList);
            }

            expando.Data = dataList;

            Dictionary<string, object?> coreList = [];
            var resourceSet = Resources.GlobalResource.ResourceManager.GetResourceSet(CultureInfo.CurrentCulture, true, true);
            if (resourceSet is not null)
            {
                foreach (DictionaryEntry item in resourceSet)
                {
                    var key = item.Key.ToString();
                    if (key is null)
                    {
                        continue;
                    }

                    var lst = key.Replace("_Name", string.Empty, StringComparison.OrdinalIgnoreCase).Split("_", 2, StringSplitOptions.RemoveEmptyEntries);
                    if (lst.Length == 1)
                    {
                        coreList.Add(lst[0], item.Value);
                    }
                    else
                    {
                        if (coreList.TryGetValue(lst[0], out var value))
                        {
                            _ = (value as Dictionary<string, object?>)?.TryAdd(lst[1], item.Value);
                        }
                        else
                        {
                            coreList.Add(lst[0], new Dictionary<string, object?> { { lst[1], item.Value } });
                        }
                    }
                }
            }

            expando.Core = coreList;

            return expando;
        }

        public static void LogException(this ILogger logger, Exception exc, [CallerMemberName] string? methodName = "") =>
#pragma warning disable CA2254 // Template should be a static expression
            logger.LogError(exc, methodName);
#pragma warning restore CA2254 // Template should be a static expression

        internal static string? PrepareResourcePath(this string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            const string resourceAssembly = ".Resource";

            var str = UiDotRegex().Replace(path, resourceAssembly + "$0");
            str = UiCommaRegex().Replace(str, resourceAssembly + ",");

            return str
                .Replace(".Infrastructure.", resourceAssembly + ".Infrastructure.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Infrastructure,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Infrastructure.Interface", resourceAssembly + ".Infrastructure.Interface", StringComparison.OrdinalIgnoreCase)
                .Replace(".Infrastructure.Interface,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Application.Service.", resourceAssembly + ".Application.Service.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Application.Service,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Application.Interface.", resourceAssembly + ".Application.Interface.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Application.Interface,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Presentation.ViewModel.", resourceAssembly + ".Presentation.ViewModel.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Presentation.ViewModel,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Shared.", resourceAssembly + ".Shared.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Shared,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Data.", resourceAssembly + ".Data.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Data,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Domain.", resourceAssembly + ".Domain.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Domain,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)

                .Replace(".Common.", resourceAssembly + ".Common.", StringComparison.OrdinalIgnoreCase)
                .Replace(".Common,", resourceAssembly + ",", StringComparison.OrdinalIgnoreCase)
                .Replace(resourceAssembly + resourceAssembly, resourceAssembly, StringComparison.OrdinalIgnoreCase);
        }

        internal static string? GetLocalizedValueInternal(DisplayAttribute displayAttribute, string propertyName, Constants.ResourceKey resourceKey, ResourceManager? cachedResourceManager = null, MemberInfo? member = null)
        {
            var result = resourceKey switch
            {
                Constants.ResourceKey.ShortName => displayAttribute.ShortName,
                Constants.ResourceKey.Description => displayAttribute.Description,
                Constants.ResourceKey.Prompt => displayAttribute.Prompt,
                Constants.ResourceKey.GroupName => displayAttribute.GroupName,
                _ => displayAttribute.Name,
            };
            if (result is not null)
            {
                return result;
            }

            if (cachedResourceManager is null)
            {
                if (displayAttribute.ResourceType is not null)
                {
                    cachedResourceManager = new ResourceManager(displayAttribute.ResourceType);
                }
                else if (displayAttribute.ResourceTypeName is not null)
                {
                    var type = Type.GetType(displayAttribute.ResourceTypeName) ?? throw new ArgumentException(nameof(DisplayAttribute.ResourceTypeName));
                    cachedResourceManager = new ResourceManager(type);
                }
                else if (member is not null)
                {
                    var type = Type.GetType(member.DeclaringType?.AssemblyQualifiedName.PrepareResourcePath()!);
                    if (type is not null)
                    {
                        cachedResourceManager = new ResourceManager(type);
                    }
                }
            }

            if (cachedResourceManager is not null)
            {
                if (displayAttribute.EnumType is null)
                {
                    result = cachedResourceManager.GetString($"{propertyName}_{resourceKey}");
                }
                else
                {
                    result = cachedResourceManager.GetString($"{displayAttribute.EnumType.Name}_{propertyName}_{resourceKey}");

                    if (result.IsNullOrEmpty() && resourceKey == Constants.ResourceKey.Name)
                    {
                        result = cachedResourceManager.GetString($"{displayAttribute.EnumType.Name}_{propertyName}");
                    }
                }
            }

            return result;
        }

        internal static RouteValueDictionary PrepareValues(object? routeValues, string? area = null)
        {
            var rootValueDictionary = new RouteValueDictionary(routeValues);
            if (!rootValueDictionary.ContainsKey(Constants.LanguageIdentifier))
            {
                rootValueDictionary.Add(Constants.LanguageIdentifier, CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            }
            else
            {
                rootValueDictionary[Constants.LanguageIdentifier] = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
            }

            if (!string.IsNullOrEmpty(area))
            {
                if (!rootValueDictionary.ContainsKey(Constants.AreaIdentifier))
                {
                    rootValueDictionary.Add(Constants.AreaIdentifier, area);
                }
                else
                {
                    rootValueDictionary[Constants.AreaIdentifier] = area;
                }
            }

            return rootValueDictionary;
        }

        [GeneratedRegex(".UI..*?,")]
        private static partial Regex UiCommaRegex();

        [GeneratedRegex(".UI..*?.")]
        private static partial Regex UiDotRegex();

        #region Inner Classes

        private sealed class MyPersianCalendar : PersianCalendar
        {
            public override int GetYear(DateTime time)
            {
                try
                {
                    return base.GetYear(time);
                }
                catch
                {
                    return time.Year;
                }
            }

            public override int GetMonth(DateTime time)
            {
                try
                {
                    return base.GetMonth(time);
                }
                catch
                {
                    return time.Month;
                }
            }

            public override int GetDayOfMonth(DateTime time)
            {
                try
                {
                    return base.GetDayOfMonth(time);
                }
                catch
                {
                    return time.Day;
                }
            }

            public override int GetDayOfYear(DateTime time)
            {
                try
                {
                    return base.GetDayOfYear(time);
                }
                catch
                {
                    return time.DayOfYear;
                }
            }

            public override System.DayOfWeek GetDayOfWeek(DateTime time)
            {
                try
                {
                    return base.GetDayOfWeek(time);
                }
                catch
                {
                    return time.DayOfWeek;
                }
            }
        }

        #endregion
    }
}
