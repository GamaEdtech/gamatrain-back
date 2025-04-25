namespace GamaEdtech.Common.Core.Extensions
{
    using System.Globalization;
    public static class DateTimeHelper
    {
        public static DateTime SystemNow() => DateTime.Now;
        public static string ToShamsiDate(this DateTime value)
        {
            PersianCalendar pc = new();
            return pc.GetYear(value) + "/" + pc.GetMonth(value) + "/" + pc.GetDayOfMonth(value);
        }
        public static string ToShamsiDate(this DateTimeOffset value)
        {
            PersianCalendar pc = new();
            return pc.GetYear(value.DateTime) + "/" + pc.GetMonth(value.DateTime) + "/" + pc.GetDayOfMonth(value.DateTime);
        }
        public static string ToPersionExactTime(this DateTime value)
        {
            PersianCalendar pc = new();
            return
                pc.GetHour(value).ToString() + ":"
                + pc.GetMinute(value).ToString();
        }
        public static string ToShamsiDateAndExactTime(this DateTime value)
        {
            PersianCalendar pc = new();
            return $"[{pc.GetYear(value)}/{pc.GetMonth(value)}/{pc.GetDayOfMonth(value)} ({pc.GetHour(value)}:{pc.GetMinute(value)}:{pc.GetSecond(value)})]";
        }
        private static SolarDayOfWeek? ToSolarDayOfWeek(this DayOfWeek dayOfWeek) => dayOfWeek switch
        {
            DayOfWeek.Sunday => (SolarDayOfWeek?)SolarDayOfWeek.یکشنبه,
            DayOfWeek.Monday => (SolarDayOfWeek?)SolarDayOfWeek.دوشنبه,
            DayOfWeek.Tuesday => (SolarDayOfWeek?)SolarDayOfWeek.سهشنبه,
            DayOfWeek.Wednesday => (SolarDayOfWeek?)SolarDayOfWeek.چهارشنبه,
            DayOfWeek.Thursday => (SolarDayOfWeek?)SolarDayOfWeek.پنجشنبه,
            DayOfWeek.Friday => (SolarDayOfWeek?)SolarDayOfWeek.جمعه,
            DayOfWeek.Saturday => (SolarDayOfWeek?)SolarDayOfWeek.شنبه,
            _ => null,
        };
        private static SolarMonthName? ToSolarMonthName(this int monthNumber) => monthNumber switch
        {
            1 => (SolarMonthName?)SolarMonthName.فروردین,
            2 => (SolarMonthName?)SolarMonthName.اردیبهشت,
            3 => (SolarMonthName?)SolarMonthName.خرداد,
            4 => (SolarMonthName?)SolarMonthName.تیر,
            5 => (SolarMonthName?)SolarMonthName.مرداد,
            6 => (SolarMonthName?)SolarMonthName.شهریور,
            7 => (SolarMonthName?)SolarMonthName.مهر,
            8 => (SolarMonthName?)SolarMonthName.آبان,
            9 => (SolarMonthName?)SolarMonthName.آذر,
            10 => (SolarMonthName?)SolarMonthName.دی,
            11 => (SolarMonthName?)SolarMonthName.بهمن,
            12 => (SolarMonthName?)SolarMonthName.اسفند,
            _ => null,
        };
        public static LunarMonthName? ToLunarMonthName(this int monthNumber) => monthNumber switch
        {
            1 => (LunarMonthName?)LunarMonthName.محرم,
            2 => (LunarMonthName?)LunarMonthName.صفر,
            3 => (LunarMonthName?)LunarMonthName.ربیعالاوّل,
            4 => (LunarMonthName?)LunarMonthName.ربیعالثانی,
            5 => (LunarMonthName?)LunarMonthName.جُمادیالاَوَّل,
            6 => (LunarMonthName?)LunarMonthName.جُمادیالثّانی,
            7 => (LunarMonthName?)LunarMonthName.رَجَب,
            8 => (LunarMonthName?)LunarMonthName.شعبان,
            9 => (LunarMonthName?)LunarMonthName.رمضان,
            10 => (LunarMonthName?)LunarMonthName.شوال,
            11 => (LunarMonthName?)LunarMonthName.ذیقعده,
            12 => (LunarMonthName?)LunarMonthName.ذیالحِجّه,
            _ => null,
        };
        public static CustomDateTimeFormat ToCustomShamsiDateAndExactTime(this DateTime value, CustomDateFormat customDateFormat)
        {
            PersianCalendar pc = new();
            return new CustomDateTimeFormat
            {
                DayOfWeak = pc.GetDayOfWeek(value).ToSolarDayOfWeek()?.ToDisplay() ?? "Unknown Day",
                MonthName = pc.GetMonth(value).ToSolarMonthName()?.ToDisplay() ?? "Unknown Month",
                Year = pc.GetYear(value).ToString().En2Fa() ?? "",
                Month = pc.GetMonth(value).ToString().En2Fa() ?? "",
                Day = pc.GetDayOfMonth(value).ToString().En2Fa() ?? "",
                Hour = pc.GetHour(value).ToString().En2Fa() ?? "",
                Minutes = pc.GetMinute(value).ToString().En2Fa() ?? "",
                Second = pc.GetSecond(value).ToString().En2Fa() ?? "",
                Millisecond = pc.GetMilliseconds(value).ToString().En2Fa() ?? "",
                TimeInterval = value.GetTimeIntervalWithNow(customDateFormat) ?? ""
            };
        }
        public static CustomDateTimeFormat ToCustomGregorianDateAndExactTime(this DateTime value, CustomDateFormat customDateFormat) => new()
        {
            DayOfWeak = value.DayOfWeek.ToString() ?? "",
            Year = value.Year.ToString() ?? "",
            Month = value.Month.ToString() ?? "",
            Day = value.Day.ToString() ?? "",
            Hour = value.Hour.ToString() ?? "",
            Minutes = value.Minute.ToString() ?? "",
            Second = value.Second.ToString() ?? "",
            Millisecond = value.Millisecond.ToString() ?? "",
            TimeInterval = value.GetTimeIntervalWithNow(customDateFormat) ?? ""
        };
        public static CustomDateTimeFormat ToCustomLunarDateAndExactTime(this DateTime value, CustomDateFormat customDateFormat)
        {
            HijriCalendar pc = new();
            return new CustomDateTimeFormat
            {
                DayOfWeak = pc.GetDayOfWeek(value).ToDisplay() ?? "",
                Year = pc.GetYear(value).ToString() ?? "",
                Month = pc.GetMonth(value).ToLunarMonthName().ToString() ?? "Unknown Month",
                Day = pc.GetDayOfMonth(value).ToString() ?? "",
                Hour = pc.GetHour(value).ToString() ?? "",
                Minutes = pc.GetMinute(value).ToString() ?? "",
                Second = pc.GetSecond(value).ToString() ?? "",
                Millisecond = pc.GetMilliseconds(value).ToString() ?? "",
                TimeInterval = value.GetTimeIntervalWithNow(customDateFormat) ?? ""
            };
        }
        public static string ToEpochTime(this DateTime dateTime)
        {
            var t = dateTime - DateTime.UnixEpoch;
            var secondsSinceEpoch = (long)t.TotalSeconds;
            return secondsSinceEpoch.ToString();
        }

        public static DateTime EpochToDateTime(this string epochTime)
        {
            var dtDateTime = DateTime.UnixEpoch.AddSeconds(double.Parse(epochTime)).ToLocalTime();
            return dtDateTime;
        }
        public static CustomDateTimeFormat EpochToCustomDateTimeFormat(this string epochTime)
        {
            var dtDateTime = DateTime.UnixEpoch.AddSeconds(double.Parse(epochTime)).ToLocalTime();
            return dtDateTime.ConvertToCustomDate(CustomDateFormat.ToSolarDate);
        }

        public static CustomDateTimeFormat ConvertToCustomDate(this DateTime dateTime, CustomDateFormat customDateFormat) => dateTime == default
                ? new CustomDateTimeFormat()
                : customDateFormat switch
                {
                    CustomDateFormat.ToSolarDate => dateTime.ToCustomShamsiDateAndExactTime(customDateFormat),
                    CustomDateFormat.ToGregorianDate => dateTime.ToCustomGregorianDateAndExactTime(customDateFormat),
                    CustomDateFormat.ToLunarDate => dateTime.ToCustomLunarDateAndExactTime(customDateFormat),
                    CustomDateFormat.ToEpochTime => new CustomDateTimeFormat { EpochTime = dateTime.ToEpochTime() ?? "" },
                    CustomDateFormat.ToUTCTime => new CustomDateTimeFormat { UTCTime = dateTime.ToUniversalTime().ToString() ?? "" },
                    _ => new CustomDateTimeFormat { },
                };
        public static CustomDateTimeFormat? ConvertTSoCustomDate(this DateTimeOffset? dateTime, CustomDateFormat customDateFormat) => customDateFormat switch
        {
            CustomDateFormat.ToSolarDate => dateTime?.DateTime.ToCustomShamsiDateAndExactTime(customDateFormat),
            CustomDateFormat.ToGregorianDate => dateTime?.DateTime.ToCustomGregorianDateAndExactTime(customDateFormat),
            CustomDateFormat.ToLunarDate => dateTime?.DateTime.ToCustomLunarDateAndExactTime(customDateFormat),
            CustomDateFormat.ToEpochTime => (CustomDateTimeFormat?)new CustomDateTimeFormat { EpochTime = dateTime?.DateTime.ToEpochTime() ?? "" },
            CustomDateFormat.ToUTCTime => (CustomDateTimeFormat?)new CustomDateTimeFormat { UTCTime = dateTime?.DateTime.ToUniversalTime().ToString() ?? "" },
            _ => (CustomDateTimeFormat?)new CustomDateTimeFormat { },
        };
        private static string? GetTimeIntervalWithNow(this DateTime dateTime, CustomDateFormat customDateFormat)
        {
            const int second = 1;
            const int minute = 60 * second;
            const int hour = 60 * minute;
            const int day = 24 * hour;
            const int month = 30 * day;
            var ts = new TimeSpan(DateTime.Now.Ticks - dateTime.Ticks);
            var delta = Math.Abs(ts.TotalSeconds);
            var deltaOriginal = ts.TotalSeconds;
            switch (customDateFormat)
            {
                case CustomDateFormat.ToSolarDate:
                {
                    if (delta < 1 * minute)
                    {
                        return deltaOriginal > 0
                            ? (ts.Seconds == 1 ? "لحظه ای قبل" : ts.Seconds.ConvertToPersianString().Trim() + " ثانیه قبل")
                            : (Math.Abs(ts.Seconds) == 1 ? "لحظاتی دیگر" : ts.Seconds.ConvertToPersianString().Trim() + " ثانیه قبل");
                    }
                    else if (delta < 2 * minute)
                    {
                        return deltaOriginal > 0 ? "یک دقیقه قبل" : "یک دقیقه دیگر";
                    }
                    else if (delta < 45 * minute)
                    {
                        return deltaOriginal > 0
                            ? ts.Minutes.ConvertToPersianString().Trim() + " دقیقه قبل"
                            : Math.Abs(ts.Minutes).ConvertToPersianString().Trim() + " دقیقه دیگر";
                    }
                    else if (delta < 90 * minute)
                    {
                        return deltaOriginal > 0 ? "یک ساعت قبل" : "یک ساعت دیگر";
                    }
                    else if (delta < 24 * hour)
                    {
                        return deltaOriginal > 0
                            ? ts.Hours.ConvertToPersianString().Trim() + " ساعت قبل"
                            : Math.Abs(ts.Hours).ConvertToPersianString().Trim() + " ساعت دیگر";
                    }
                    else if (delta < 48 * hour)
                    {
                        return deltaOriginal > 0 ? "دیروز" : "فردا";
                    }
                    else if (delta < 30 * day)
                    {
                        return deltaOriginal > 0
                            ? ts.Days.ConvertToPersianString().Trim() + " روز قبل"
                            : Math.Abs(ts.Days).ConvertToPersianString().Trim() + " روز دیگر";
                    }
                    else if (delta < 12 * month)
                    {
                        if (deltaOriginal > 0)
                        {
                            var months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                            return months <= 1 ? "یک ماه قبل" : months.ConvertToPersianString().Trim() + " ماه قبل";
                        }
                        else
                        {
                            var months = Math.Abs(Convert.ToInt32(Math.Floor((double)ts.Days / 30)));
                            return months <= 1 ? "یک ماه دیگر" : months.ConvertToPersianString().Trim() + " ماه دیگر";
                        }
                    }
                    else
                    {
                        if (deltaOriginal > 0)
                        {
                            var years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
                            return years <= 1 ? "یک سال قبل" : years.ConvertToPersianString() + " سال قبل";
                        }
                        else
                        {
                            var years = Math.Abs(Convert.ToInt32(Math.Floor((double)ts.Days / 365)));
                            return years <= 1 ? "یک سال دیگر" : years.ConvertToPersianString() + " سال دیگر";
                        }
                    }
                }
                case CustomDateFormat.ToGregorianDate:
                {
                    if (delta < 1 * minute)
                    {
                        return deltaOriginal > 0
                            ? ts.Seconds <= 1 ? "a moment ago" : ts.Seconds + " Seconds ago"
                            : Math.Abs(ts.Seconds) <= 1 ? "one more moment" : Math.Abs(ts.Seconds) + " more seconds";
                    }
                    else if (delta < 2 * minute)
                    {
                        return deltaOriginal > 0 ? "a minute ago" : "a few more minutes";
                    }
                    else if (delta < 45 * minute)
                    {
                        return deltaOriginal > 0 ? ts.Minutes + " minutes ago" : Math.Abs(ts.Minutes) + " more minutes";
                    }
                    else if (delta < 90 * minute)
                    {
                        return deltaOriginal > 0 ? "an hour ago" : "one more hour";
                    }
                    else if (delta < 24 * hour)
                    {
                        return deltaOriginal > 0 ? ts.Hours + " hours ago" : Math.Abs(ts.Hours) + " more hours";
                    }
                    else if (delta < 48 * hour)
                    {
                        return deltaOriginal > 0 ? "yesterday" : "tomorrow";
                    }
                    else if (delta < 30 * day)
                    {
                        return deltaOriginal > 0 ? ts.Days + " days ago" : Math.Abs(ts.Days) + " more days";
                    }
                    else if (delta < 12 * month)
                    {
                        if (deltaOriginal > 0)
                        {
                            var months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                            return months <= 1 ? "a month ago" : months + " months ago";
                        }
                        else
                        {
                            var months = Math.Abs(Convert.ToInt32(Math.Floor((double)ts.Days / 30)));
                            return months <= 1 ? "one more month" : months + " more months";
                        }
                    }
                    else
                    {
                        if (deltaOriginal > 0)
                        {
                            var years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
                            return years <= 1 ? "a year ago" : years + " years ago";
                        }
                        else
                        {
                            var years = Math.Abs(Convert.ToInt32(Math.Floor((double)ts.Days / 365)));
                            return years <= 1 ? "one more year" : years + "more years";
                        }
                    }
                }
                case CustomDateFormat.ToLunarDate:
                {
                    if (delta < 1 * minute)
                    {
                        return deltaOriginal > 0
                            ? ts.Seconds <= 1 ? "منذ لحظة" : ts.Seconds.ToString().En2Fa() + " منذ ثوانى"
                            : Math.Abs(ts.Seconds) <= 1 ? "لحظة أخرى" : Math.Abs(ts.Seconds).ToString().En2Fa() + " ثوانٍ أخرى";
                    }
                    else if (delta < 2 * minute)
                    {
                        return deltaOriginal > 0 ? "قبل دقيقة" : "المزيد من دقيقة واحدة";
                    }
                    else if (delta < 45 * minute)
                    {
                        return deltaOriginal > 0 ? ts.Minutes.ToString().En2Fa() + " قبل دقيقة" : Math.Abs(ts.Minutes).ToString().En2Fa() + " دقائق أخرى";
                    }
                    else if (delta < 90 * minute)
                    {
                        return deltaOriginal > 0 ? "قبل ساعة" : "ساعات أخرى";
                    }
                    else if (delta < 24 * hour)
                    {
                        return deltaOriginal > 0 ? ts.Hours.ToString().En2Fa() + " منذ ساعات" : Math.Abs(ts.Hours).ToString().En2Fa() + " ساعات أخرى";
                    }
                    else if (delta < 48 * hour)
                    {
                        return deltaOriginal > 0 ? "في الامس" : "غدا";
                    }
                    else if (delta < 30 * day)
                    {
                        return deltaOriginal > 0 ? ts.Days.ToString().En2Fa() + " أيام مضت" : Math.Abs(ts.Days).ToString().En2Fa() + " أيام أخرى";
                    }
                    else if (delta < 12 * month)
                    {
                        if (deltaOriginal > 0)
                        {
                            var months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                            return months <= 1 ? "قبل شهر" : months.ToString().En2Fa() + " منذ اشهر";
                        }
                        else
                        {
                            var months = Math.Abs(Convert.ToInt32(Math.Floor((double)ts.Days / 30)));
                            return months <= 1 ? "بعد شهر واحد" : months.ToString().En2Fa() + " شهور أخرى";
                        }
                    }
                    else
                    {
                        if (deltaOriginal > 0)
                        {
                            var years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
                            return years <= 1 ? "قبل عام" : years.ToString().En2Fa() + " منذ سنوات";
                        }
                        else
                        {
                            var years = Math.Abs(Convert.ToInt32(Math.Floor((double)ts.Days / 365)));
                            return years <= 1 ? "سنة أخرى" : years.ToString().En2Fa() + " سنوات أخرى";
                        }
                    }

                }
                default:
                    return null;
            }
        }
    }
    #region Struct And Events
    public struct CustomDateTimeFormat
    {
        public string DayOfWeak { get; set; }
        public string MonthName { get; set; }
        public string Year { get; set; }
        public string Month { get; set; }
        public string Day { get; set; }
        public string Hour { get; set; }
        public string Minutes { get; set; }
        public string Second { get; set; }
        public string Millisecond { get; set; }
        public string EpochTime { get; set; }
        public string UTCTime { get; set; }
        public string TimeInterval { get; set; }

        public readonly bool HasValue() => DayOfWeak.HasValue() &&
                 MonthName.HasValue() &&
                 Year.HasValue() &&
                 Month.HasValue() &&
                 Day.HasValue() &&
                 Hour.HasValue() &&
                 Minutes.HasValue() &&
                 Second.HasValue() &&
                 Millisecond.HasValue() &&
                 EpochTime.HasValue() &&
                 UTCTime.HasValue() &&
                 TimeInterval.HasValue();
    }
    public enum SolarDayOfWeek
    {
        //
        // Summary:
        //     Indicates Sunday.
        یکشنبه = 0,
        //
        // Summary:
        //     Indicates Monday.
        دوشنبه = 1,
        //
        // Summary:
        //     Indicates Tuesday.
        سهشنبه = 2,
        //
        // Summary:
        //     Indicates Wednesday.
        چهارشنبه = 3,
        //
        // Summary:
        //     Indicates Thursday.
        پنجشنبه = 4,
        //
        // Summary:
        //     Indicates Friday.
        جمعه = 5,
        //
        // Summary:
        //     Indicates Saturday.
        شنبه = 6
    }
    public enum SolarMonthName
    {
        None = 0,
        فروردین = 1,
        اردیبهشت = 2,
        خرداد = 3,
        تیر = 4,
        مرداد = 5,
        شهریور = 6,
        مهر = 7,
        آبان = 8,
        آذر = 9,
        دی = 10,
        بهمن = 11,
        اسفند = 12
    }
    public enum LunarMonthName
    {
        None = 0,
        محرم = 1,
        صفر = 2,
        ربیع‌الاوّل = 3,
        ربیع‌الثانی = 4,
        جُمادی‌الاَوَّل = 5,
        جُمادی‌الثّانی = 6,
        رَجَب = 7,
        شعبان = 8,
        رمضان = 9,
        شوال = 10,
        ذیقعده = 11,
        ذی‌الحِجّه = 12
    }
    public enum CustomDateFormat
    {
        /// <summary>
        /// Explain: تاریخ شمسی
        /// </summary>
        ToSolarDate,
        /// <summary>
        /// Explain: تاریخ میلادی
        /// </summary>
        ToGregorianDate,
        /// <summary>
        /// Explain: تاریخ قمری
        /// </summary>
        ToLunarDate,
        /// <summary>
        /// Explain: Epoch Time
        /// </summary>
        ToEpochTime,
        /// <summary>
        /// Explain: UTC time
        /// </summary>
        ToUTCTime
    }
    #endregion
}
