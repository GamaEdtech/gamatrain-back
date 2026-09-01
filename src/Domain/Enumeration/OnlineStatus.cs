namespace GamaEdtech.Domain.Enumeration
{
    using System;

    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class OnlineStatus : Enumeration<OnlineStatus, byte>
    {
        [Display]
        public static readonly OnlineStatus Online = new(nameof(Online), 0);

        [Display]
        public static readonly OnlineStatus ActiveRecently = new(nameof(ActiveRecently), 1);

        [Display]
        public static readonly OnlineStatus OnlineToday = new(nameof(OnlineToday), 2);

        [Display]
        public static readonly OnlineStatus ActiveThisWeek = new(nameof(ActiveThisWeek), 3);

        [Display]
        public static readonly OnlineStatus ActiveThisMonth = new(nameof(ActiveThisMonth), 4);

        [Display]
        public static readonly OnlineStatus ActiveLongTimeAgo = new(nameof(ActiveLongTimeAgo), 5);

        [Display]
        public static readonly OnlineStatus NewUser = new(nameof(NewUser), 6);

        public static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(5);

        public static readonly TimeSpan ActiveRecentlyThreshold = TimeSpan.FromHours(1);

        public static readonly TimeSpan OnlineTodayThreshold = TimeSpan.FromHours(24);

        public static readonly TimeSpan ActiveThisWeekThreshold = TimeSpan.FromDays(7);

        public static readonly TimeSpan ActiveThisMonthThreshold = TimeSpan.FromDays(30);

        public OnlineStatus()
        {
        }

        public OnlineStatus(string name, byte value) : base(name, value)
        {
        }

        public static OnlineStatus Calculate(DateTimeOffset? loginDate)
        {
            if (!loginDate.HasValue)
            {
                return NewUser;
            }

            var diff = DateTimeOffset.UtcNow.Subtract(loginDate.Value);
            if (diff <= OnlineThreshold)
            {
                return Online;
            }
            if (diff <= ActiveRecentlyThreshold)
            {
                return ActiveRecently;
            }
            if (diff <= OnlineTodayThreshold)
            {
                return OnlineToday;
            }
            if (diff <= ActiveThisWeekThreshold)
            {
                return ActiveThisWeek;
            }
            if (diff <= ActiveThisMonthThreshold)
            {
                return ActiveThisMonth;
            }

            _ = diff;   //bypass analyzer
            return ActiveLongTimeAgo;
        }
    }
}
