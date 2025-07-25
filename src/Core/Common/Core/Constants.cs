#pragma warning disable CA1720 // Identifier contains type name
namespace GamaEdtech.Common.Core
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Resources;

    public static class Constants
    {
        public const string Gotcha = "Gotcha";

        public const int NullInteger = -1;
        public const string Delimiter = "___";
        public const string DelimiterAlternate = "|";
        public const char JoinDelimiter = ',';
        public const int ForeignKeySqlException = 547;
        public const int DuplicatePrimaryKeySqlException = 2627;
        public const int DuplicateKeySqlException = 2601;
        public const string ValidImageExtensions = "png,jpg,jpeg,gif,webp,svg";
        public const string ValidDocumentExtensions = "pdf,doc,docx";
        public const string ValidVideoExtensions = "mpeg,avi";
        public const string ValidWebExtensions = "zip";
        public const string ValidImageMimeTypes = "image/png,image/jpeg,image/gif";
        public const string ValidDocumentMimeTypes = "application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        public const string ValidVideoMimeTypes = "audio/mpeg,video/x-msvideo";
        public const string ValidZipMimeTypes = "application/zip,application/x-zip-compressed,application/x-7z-compressed,application/x-bzip,application/x-bzip2,application/gzip,application/x-rar-compressed";
        public const string ValidAndroidMimeTypes = "application/vnd.android.package-archive,application/zip,application/x-zip-compressed";
        public const string ValidIosMimeTypes = "application/octet-stream,application/zip,application/x-zip-compressed";
        public const string ValidWindowsMimeTypes = "application/x-silverlight-app,application/zip,application/x-zip-compressed";
        public const string DefaultLanguageCode = "en";
        public const string LanguageIdentifier = "culture";
        public const string AreaIdentifier = "area";
        public const int TotalRecords = 500000;
        public const string ReportSearchFieldPrefix = "Report_";
        public const string XForwardedFor = "X-Forwarded-For";
        public const string ArchiveIdentifier = "Archive";
        public const string ActionsIdentifier = "Actions";
        public const string MigrateIdentifier = "Migrate";
        public const string LineBreak = "[BR]";
        public const string HierarchyValueDelimiter = "/";
        public const string PasswordIdentifier = "******";
        public const string PasswordIdentifierWithData = "************";
        public const string SchemaIdentifier = "[SCHEMA]";
        public const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        public const string Cookie = "__f";
        public const string AspNetApplicationCookie = "__fapp";
        public const string RequestVerificationTokenCookie = "__frvt";
        public const int DisplayOrder = 10000;
        public const string ControllerPostfix = "Controller";
        public const string PagePostfix = "Model";
        public const string UtcTimeZoneId = "Coordinated Universal Time";
        public const string TimeZoneIdClaim = "TimeZoneId";
        public const string SchoolIdClaim = "SchoolId";
        public const string CityIdClaim = "CityId";
        public static readonly TimeSpan BaseUtcOffset = new(0, 0, 0);

        internal const string HttpClientIgnoreSslAndAutoRedirect = "HttpClientIgnoreSslAndAutoRedirect";
        internal const string HttpClientIgnoreSslAndAutoRedirectTls13 = "HttpClientIgnoreSslAndAutoRedirectTls13";
        public static string? ErrorCodePrefix { get; set; }

        [JsonConverter(typeof(EnumStringConverter<EntityType>))]
        public enum EntityType
        {
            ApplicationSetting = 0,
            ApplicationUser = 1,
            ApplicationUserClaim = 2,
        }

        public enum OperationResult : byte
        {
            NotFound = 0,
            Succeeded = 1,
            Failed = 2,
            Duplicate = 3,
            NotValid = 4,
        }

        public enum OperandType : byte
        {
            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            Equals = 0,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            GreaterThan = 1,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            LessThan = 2,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            GreaterThanOrEqual = 3,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            LessThanOrEqual = 4,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            Contains = 5,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            StartsWith = 6,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            EndsWith = 7,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(OperandType))]
            NotEquals = 8,
        }

        public enum EntityState : byte
        {
            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(EntityState))]
            Detached = 0,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(EntityState))]
            Unchanged = 1,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(EntityState))]
            Added = 2,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(EntityState))]
            Deleted = 3,

            [Display(ResourceType = typeof(GlobalResource), EnumType = typeof(EntityState))]
            Modified = 4,
        }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum SortType
        {
            Asc,
            Desc,
        }

        public enum FormatProvider
        {
            CurrentCulture,
            InvariantCulture,
        }

        internal enum ResourceKey
        {
            Name,
            ShortName,
            Description,
            Prompt,
            GroupName,
        }
    }
}
#pragma warning restore CA1720 // Identifier contains type name
