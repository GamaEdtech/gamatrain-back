namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using SkiaSharp;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>A teacher adding, editing and deleting their own papers, and uploading their files, by gama-api's rules
    /// (<c>Test_lib::add</c>, <c>edit</c>, <c>delete</c>, <c>prepareDataToSave</c>, <c>checkRequiredFields</c>). New papers
    /// take the next id after the highest one, so after the last copy from gama-api they continue above its ids. Files go to
    /// the private bucket's <c>PastPaper</c> container and are kept there when a paper or file is deleted.</summary>
    public sealed partial class NativePastPaperSource
    {
        /// <summary>The largest file a teacher uploads for a paper. gama-api allows 500 MB, but an upload is held in memory
        /// here; papers are rarely more than a few MB.</summary>
        public const long MaxUploadBytes = 200L * 1024 * 1024;

        private const string UploadPurpose = "PastPaperUpload";
        private const int ThumbnailWidth = 600;
        private const int SaveAttempts = 3;
        private static readonly TimeSpan UploadLifetime = TimeSpan.FromDays(1);
        private static readonly TimeSpan AddInterval = TimeSpan.FromSeconds(30);

        private static readonly string[] PdfExtensions = ["pdf"];
        private static readonly string[] WordExtensions = ["doc", "docx"];
        private static readonly string[] AnswerExtensions = ["pdf", "doc", "docx"];
        private static readonly string[] ExtraExtensions = ["mp4", "avi", "m4a", "mpg", "flv", "doc", "docx", "ppt", "pptx", "pdf", "mp3", "wav", "wave", "acc", "zip", "rar"];

        // The fields a client may send (Test_lib::CLIENT_FIELDS, without the managers' uid and price_multiplier).
        private static readonly string[] ClientFields =
        [
            "section", "base", "course", "lesson", "topic", "topics", "test_type", "answer_type", "level", "holding_level", "state",
            "state_multi", "area", "school", "edu_year", "edu_month", "edu_session", "variant", "code", "title", "title_encoded",
            "description", "free_agreement", "file_pdf", "file_word", "file_answer", "file_extra",
        ];

        // Classifications that need no answer type, need only a year, or take no topics (the term exams).
        private static readonly string[] NoAnswerTypeClassifications = ["91", "10478", "10479"];
        private static readonly string[] YearOnlyClassifications = ["380", "10258"];
        private static readonly string[] TermClassifications = ["89", "90"];

        // gama-api's titleCorrection/descriptionCorrection replacements on Gamatrain, in its order.
        private static readonly (string From, string To)[] Corrections =
        [
            ("*", ""), ("&#39;", "'"), ("<p><br data-mce-bogus=\"1\"></p>", ""), ("ك", "ک"), ("ي", "ی"), ("ى", "ی"),
            ("۱", "1"), ("۲", "2"), ("۳", "3"), ("۴", "4"), ("۵", "5"), ("۶", "6"), ("۷", "7"), ("۸", "8"), ("۹", "9"), ("۰", "0"),
            ("٠", "0"), ("١", "1"), ("٢", "2"), ("٣", "3"), ("٤", "4"), ("٥", "5"), ("٦", "6"), ("٧", "7"), ("٨", "8"), ("٩", "9"),
            ("!", ""), ("#", ""), ("^", ""), (";", ","), ("`", ""), ("~", ""), ("+", "-"), ("<p>&nbsp;</p>", ""),
        ];

        /// <summary>What an upload's key carries: the uploader, the stored file, and for a PDF its pages and thumbnail.</summary>
        private sealed record UploadedFile(long UserId, string StoredPath, long Size, string FileExtension, short? Pages, string? Thumbnail);

        public async Task<ResultData<JsonNode>> UploadPaperFileAsync([NotNull] TeacherPaperUploadDto requestDto)
        {
            try
            {
                var extension = System.IO.Path.GetExtension(requestDto.FileName).TrimStart('.').ToLowerInvariant();
                if (!ExtraExtensions.Contains(extension))
                {
                    return Refuse("wrongRequest", null, "Extension is not valid.");
                }

                if (requestDto.Content.LongLength is 0 or > MaxUploadBytes)
                {
                    return Refuse("wrongRequest", null, "File size is not valid.");
                }

                short? pages = null;
                string? thumbnail = null;
                if (extension == "pdf")
                {
                    pages = PageCount(requestDto.Content);
                    if (requestDto.Kind == "pdf")
                    {
                        thumbnail = await ThumbnailAsync(requestDto.Content);
                    }
                }

                var stored = await StoreAsync(requestDto.Content, $"file.{extension}", requestDto.ContentType, ContainerType.PastPaper);
                if (stored is null)
                {
                    return Refuse("error", null, "The file could not be stored.");
                }

                UploadedFile file = new(requestDto.UserId, stored, requestDto.Content.LongLength, extension, pages, thumbnail);
                var key = dataProtectionProvider.Value.CreateProtector(UploadPurpose).ToTimeLimitedDataProtector()
                    .Protect(JsonSerializer.Serialize(file), UploadLifetime);
                return new(OperationResult.Succeeded) { Data = new JsonArray(new JsonObject { ["file"] = new JsonObject { ["name"] = key, ["size"] = file.Size } }) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonNode>> AddPaperAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            try
            {
                var body = Client(requestDto.Body);
                var userId = requestDto.UserId;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (!await HasRoleAsync(uow, userId, nameof(Role.Teacher), nameof(Role.Admin)))
                {
                    return Refuse("accessDenied", null, "Access denied.");
                }

                // Submitting for another teacher is the admins' (the admin panel's); a teacher submits only for themselves.
                if (Field(requestDto.Body, "uid") is { } uid && uid != Invariant(userId))
                {
                    return Refuse("accessDenied", "onBehalfNotAllowed", "Access denied.");
                }

                NormalizeLegacyFields(body);
                var missing = MissingFields(body, add: true);
                if (missing.Count > 0)
                {
                    return Required(missing);
                }

                var last = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.OwnerUserId == userId)
                    .OrderByDescending(t => t.CreationDate).Select(t => (DateTimeOffset?)t.CreationDate).FirstOrDefaultAsync();
                if (last is { } lastAdded && DateTimeOffset.UtcNow - lastAdded < AddInterval)
                {
                    return Refuse("wrongRequest", "rateLimit-addnew", "Please wait a little before adding another paper.");
                }

                var question = ReadUpload(Field(body, "file_pdf"), userId, PdfExtensions);
                if (question is null)
                {
                    return Required(["file_pdf"]);
                }

                var now = DateTimeOffset.UtcNow;
                PastPaper paper = new()
                {
                    CoreOwnerId = 0,
                    OwnerUserId = userId,
                    DisplayOwnerUserId = userId,
                    Status = PastPaperStatus.Pending,
                    PriceMultiplier = 1,
                    CreationDate = now,
                    LastModifyDate = now,
                    SyncDate = now,
                    Files = [],
                };
                await ApplyFieldsAsync(uow, paper, body, add: true);
                _ = ApplyFiles(paper, body, userId, question);

                var id = await SaveNewAsync(paper);
                return new(OperationResult.Succeeded) { Data = new JsonObject { ["id"] = id } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonNode>> EditPaperAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            try
            {
                var body = Client(requestDto.Body);
                if (body.Count == 0)
                {
                    return Required(["data"]);
                }

                var userId = requestDto.UserId;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.Id == requestDto.Id && !t.IsDeleted, includes: t => t.Include(p => p.Files), tracking: true)
                    .FirstOrDefaultAsync();
                if (paper is null)
                {
                    return Refuse("noResult", null, "No result for this request.");
                }

                var owner = paper.OwnerUserId == userId;
                var admin = !owner && await HasRoleAsync(uow, userId, nameof(Role.Admin));
                if (!owner && !admin)
                {
                    return Refuse("accessDenied", null, "Access denied.");
                }

                var fullEdit = admin || FullEditStatuses.Contains(paper.Status.Value);
                List<string> ignored = fullEdit ? [] : [.. body.Select(t => t.Key).Where(t => !LimitedEditFields.Contains(t))];

                NormalizeLegacyFields(body);
                var missing = MissingFields(body, add: false);
                if (missing.Count > 0)
                {
                    return Required(missing);
                }

                if (fullEdit)
                {
                    await ApplyFieldsAsync(uow, paper, body, add: false);
                }
                else if (Field(body, "description") is { } description)
                {
                    paper.Description = CorrectDescription(description);
                }

                var filesChanged = ApplyFiles(paper, body, userId, ReadUpload(Field(body, "file_pdf"), userId, PdfExtensions));

                // An owner's edit after review sends the paper back to review.
                if (paper.Status != PastPaperStatus.Pending && !admin)
                {
                    paper.Status = PastPaperStatus.Edited;
                }

                if (filesChanged)
                {
                    paper.LastModifyDate = DateTimeOffset.UtcNow;
                }

                await AssignExtraIdsAsync(uow, paper);
                _ = await uow.SaveChangesAsync();

                JsonObject result = new() { ["id"] = paper.Id, ["status"] = (int)paper.Status.Value, ["editMode"] = fullEdit ? "full" : "limited" };
                List<string> messages = [];
                if (ignored.Count > 0)
                {
                    result["ignoredFields"] = new JsonArray([.. ignored.Select(t => (JsonNode)t)]);
                    messages.Add("In its current status only the description and the files of this paper were saved, the other changes were not applied.");
                }

                if (paper.Status == PastPaperStatus.Edited && !admin)
                {
                    messages.Add("Your changes were saved and the paper will be reviewed again.");
                }

                if (messages.Count > 0)
                {
                    result["message"] = string.Join(' ', messages);
                }

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonNode>> DeletePaperAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            try
            {
                var type = string.IsNullOrEmpty(requestDto.FileType) ? "all" : requestDto.FileType;
                if (type is not ("all" or "pdf" or "word" or "answer" or "extra"))
                {
                    return Refuse("wrongRequest", null, "typeIsNotValid");
                }

                var userId = requestDto.UserId;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PastPaper>();
                var paper = type == "extra"
                    ? await repository.GetManyQueryable(t => !t.IsDeleted && t.Files!.Any(f => f.Kind == PastPaperFileKind.Extra && f.CoreExtraFileId == requestDto.Id),
                        includes: t => t.Include(p => p.Files), tracking: true).FirstOrDefaultAsync()
                    : await repository.GetManyQueryable(t => t.Id == requestDto.Id && !t.IsDeleted, includes: t => t.Include(p => p.Files), tracking: true).FirstOrDefaultAsync();
                if (paper is null)
                {
                    return Refuse("wrongRequest", null, "fileNotFound");
                }

                var admin = paper.OwnerUserId != userId && await HasRoleAsync(uow, userId, nameof(Role.Admin));
                if (paper.OwnerUserId != userId && !admin)
                {
                    return Refuse("accessDenied", null, "Access denied.");
                }

                // Once past review the owner only replaces files; admins delete in any status.
                if (!admin && LockedStatuses.Contains(paper.Status.Value))
                {
                    return Refuse("wrongRequest", "postAlreadyConfirmed", type == "all"
                        ? "This paper can not be deleted in its current status."
                        : "In its current status the files of this paper can not be deleted, only replaced.");
                }

                // The PDF is required, so it is only replaced; a paper without a PDF keeps its Word file.
                if (type == "pdf" || (type == "word" && File(paper, PastPaperFileKind.Question) is null))
                {
                    return Refuse("wrongRequest", "lastMainFile", "The main file of the paper can not be deleted. To change it, upload a replacement instead.");
                }

                // The stored files are kept: a delete only unlinks them.
                switch (type)
                {
                    case "all":
                        paper.IsDeleted = true;
                        paper.LastModifyDate = DateTimeOffset.UtcNow;
                        break;
                    case "extra":
                        _ = paper.Files!.Remove(paper.Files!.First(t => t.Kind == PastPaperFileKind.Extra && t.CoreExtraFileId == requestDto.Id));
                        break;
                    default:
                        var kind = type == "word" ? PastPaperFileKind.QuestionWord : PastPaperFileKind.Answer;
                        if (File(paper, kind) is { } file)
                        {
                            _ = paper.Files!.Remove(file);
                        }

                        break;
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        // ---- Fields ----

        /// <summary>The client fields of a request (gama-api's <c>dataMask</c>).</summary>
        private static JsonObject Client(JsonObject? body) =>
            new(body?.Where(t => ClientFields.Contains(t.Key)).Select(t => KeyValuePair.Create(t.Key, t.Value?.DeepClone())) ?? []);

        /// <summary>gama-api's <c>handleLegacyFields</c>: <c>topic</c> for <c>topics</c>, an unknown holding level as none,
        /// <c>state_multi</c> for a state-level paper, <c>title_encoded</c> for <c>title</c>.</summary>
        private static void NormalizeLegacyFields(JsonObject body)
        {
            if (IsEmpty(body["topics"]) && !IsEmpty(body["topic"]))
            {
                body["topics"] = body["topic"]!.DeepClone();
            }

            if (body.ContainsKey("holding_level") && Field(body, "holding_level") is { } level && level is not ("1" or "2" or "3" or "4" or "5"))
            {
                body["holding_level"] = string.Empty;
            }

            if (Field(body, "holding_level") == "3" && !IsEmpty(body["state_multi"]))
            {
                body["state"] = body["state_multi"]!.DeepClone();
            }

            if (Field(body, "title") is null && Field(body, "title_encoded") is { } encoded)
            {
                body["title"] = encoded;
            }
        }

        /// <summary>gama-api's <c>checkRequiredFields</c> on Gamatrain.</summary>
        internal static List<string> MissingFields(JsonObject body, bool add)
        {
            List<string> required = add ? ["section", "base", "lesson", "test_type", "title", "description", "file_pdf"] : [];
            if (Field(body, "test_type") is { } type)
            {
                if (!NoAnswerTypeClassifications.Contains(type))
                {
                    required.Add("answer_type");
                }

                if (type != "10479")
                {
                    required.AddRange(YearOnlyClassifications.Contains(type) ? ["edu_year"] : ["edu_year", "edu_month"]);
                }
            }

            required.AddRange(Field(body, "holding_level") switch
            {
                "1" => ["state", "area", "school"],
                "2" => ["state", "area"],
                "3" => ["state"],
                _ => [],
            });

            return [.. required.Distinct().Where(t => t == "answer_type" ? !IsAnswerType(Field(body, t)) : IsEmpty(body[t]))];
        }

        /// <summary>Sets the paper's fields from the request, as gama-api's <c>prepareDataToSave</c> and add/edit do: on an edit
        /// only the fields that were sent (and not empty), except that an empty <c>course</c> or <c>topics</c> clears it.</summary>
        private static async Task ApplyFieldsAsync(IUnitOfWork uow, PastPaper paper, JsonObject body, bool add)
        {
            int? Int(string key) => int.TryParse(Field(body, key), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

            if (Int("section") is int board)
            {
                paper.CoreBoardId = board;
            }

            if (Int("base") is int grade)
            {
                paper.CoreGradeId = grade;
            }

            if (Int("lesson") is int subject)
            {
                paper.CoreSubjectId = subject;
            }

            if (Int("course") is int course and > 0)
            {
                paper.CoreCourseId = course;
            }
            else if (body.ContainsKey("course"))
            {
                paper.CoreCourseId = null;
            }

            if (Int("test_type") is int paperType)
            {
                paper.CorePaperTypeId = paperType;
                paper.IsPaper = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Id == paperType)
                    .Select(t => t.IsPaper).FirstOrDefaultAsync() == true;
            }

            if (body.ContainsKey("topics") || add)
            {
                var topics = TopicList(body["topics"]);
                paper.CoreTopicIds = topics.Count == 0 ? null : string.Join('+', topics);
            }

            if (TermClassifications.Contains(Invariant(paper.CorePaperTypeId)))
            {
                paper.CoreTopicIds = null;
            }

            if (Int("answer_type") is int answerType and <= 3)
            {
                paper.AnswerType = (byte)answerType;
            }

            if (Int("level") is int level)
            {
                paper.Level = (byte)(level is >= 1 and <= 3 ? level : 2);
            }

            if (Int("edu_year") is int year and <= short.MaxValue)
            {
                paper.EduYear = (short)year;
            }

            if (Int("edu_month") is int month and <= 12)
            {
                paper.EduMonth = (byte)month;
            }

            if (Field(body, "edu_session") is { } session)
            {
                paper.EduSession = session.ToLowerInvariant() switch
                {
                    "1" or "s" => 1,
                    "2" or "w" => 2,
                    "3" or "m" => 3,
                    _ => null,
                };
            }

            if (Int("variant") is int variant and <= short.MaxValue)
            {
                paper.Variant = (short)variant;
            }

            if (!add && Field(body, "code") is { } code)
            {
                paper.Code = code.Length > 10 ? code[..10] : code;
            }

            if (Field(body, "title") is { } title)
            {
                paper.Title = CorrectTitle(title);
            }

            if (Field(body, "description") is { } description)
            {
                paper.Description = CorrectDescription(description);
            }

            if (body.ContainsKey("free_agreement") || add)
            {
                paper.FreeAgreement = !IsEmpty(body["free_agreement"]) && Field(body, "free_agreement") is not ("0" or "false");
            }

            if (body.ContainsKey("holding_level") || add)
            {
                ApplyLocation(paper, body);
            }
        }

        /// <summary>State, area and school follow the holding level: one state, area and school for a school; one state and
        /// area for an area; one or more states ("id+id") for a state; none for a country or international paper.</summary>
        private static void ApplyLocation(PastPaper paper, JsonObject body)
        {
            var level = Field(body, "holding_level");
            paper.HoldingLevel = short.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var holding) ? holding : null;
            var singleState = body["state"] is JsonArray states ? Field(states.FirstOrDefault()) : Field(body, "state");
            int? area = int.TryParse(Field(body, "area"), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
            long? school = long.TryParse(Field(body, "school"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;
            (paper.State, paper.CoreAreaId, paper.CoreSchoolId) = level switch
            {
                "1" => (singleState, area, school),
                "2" => (singleState, area, null),
                "3" => (StateList(body["state"]), null, null),
                _ => (null, null, null),
            };
        }

        /// <summary>The states of a state-level paper as "id+id": from an array, a JSON array, one id or "id+id".</summary>
        private static string? StateList(JsonNode? node)
        {
            if (Field(node) is { } text && text.StartsWith('['))
            {
                node = JsonNode.Parse(text);
            }

            List<string> ids = [.. Values(node).Where(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out _)).Select(t => t!).Distinct()];
            return ids.Count == 0 ? null : string.Join('+', ids);
        }

        private static List<string> TopicList(JsonNode? node) =>
            [.. Values(node).Where(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0).Select(t => t!).Distinct()];

        /// <summary>A list field's values: an array's items, or "id+id" / "id,id".</summary>
        private static IEnumerable<string?> Values(JsonNode? node) =>
            node is JsonArray array ? array.Select(Field) : (Field(node) ?? string.Empty).Replace(',', '+').Split('+');

        /// <summary>gama-api's <c>titleCorrection</c> (after <c>urldecode</c>): no tags, Persian/Arabic letters and digits made
        /// plain, and a few symbols removed.</summary>
        internal static string CorrectTitle(string title) => Correct(TagRegex().Replace(System.Net.WebUtility.UrlDecode(title), string.Empty)).Trim();

        /// <summary>gama-api's <c>descriptionCorrection</c> (only <c>b</c>, <c>br</c> and <c>p</c> tags kept), then this app's
        /// HTML sanitizer, as every saved user HTML.</summary>
        internal static string? CorrectDescription(string description) => Correct(NonBasicTagRegex().Replace(description, string.Empty)).SanitizeHtml();

        private static string Correct(string text)
        {
            foreach (var (from, to) in Corrections)
            {
                text = text.Replace(from, to, StringComparison.Ordinal);
            }

            return text;
        }

        // ---- Files ----

        /// <summary>Links the uploaded files the request names (keys from <see cref="UploadPaperFileAsync"/>, the caller's own):
        /// the question PDF (its pages and thumbnail), the Word file, the answer, and extra files (<c>{type, file}</c> to add,
        /// <c>{id, type[, file]}</c> to change one). Whether the question PDF or Word file changed.</summary>
        private bool ApplyFiles(PastPaper paper, JsonObject body, long userId, UploadedFile? question)
        {
            var files = paper.Files ??= [];
            var changed = false;
            if (question is not null)
            {
                SetFile(files, paper.Id, PastPaperFileKind.Question, question);
                paper.Thumbnail = question.Thumbnail ?? paper.Thumbnail;
                changed = true;
            }

            if (ReadUpload(Field(body, "file_word"), userId, WordExtensions) is { } word)
            {
                SetFile(files, paper.Id, PastPaperFileKind.QuestionWord, word);
                changed = true;
            }

            if (ReadUpload(Field(body, "file_answer"), userId, AnswerExtensions) is { } answer)
            {
                SetFile(files, paper.Id, PastPaperFileKind.Answer, answer);
            }

            foreach (var extra in (body["file_extra"] as JsonArray ?? []).OfType<JsonObject>())
            {
                int? type = int.TryParse(Field(extra, "type"), NumberStyles.None, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : null;
                var upload = ReadUpload(Field(extra, "file"), userId, ExtraExtensions);
                if (long.TryParse(Field(extra, "id"), NumberStyles.None, CultureInfo.InvariantCulture, out var extraId))
                {
                    var current = files.FirstOrDefault(f => f.Kind == PastPaperFileKind.Extra && f.CoreExtraFileId == extraId);
                    if (current is not null)
                    {
                        current.CoreTypeId = type ?? current.CoreTypeId;
                        if (upload is not null)
                        {
                            Fill(current, upload);
                        }
                    }
                }
                else if (upload is not null)
                {
                    PastPaperFile file = new() { PastPaperId = paper.Id, Kind = PastPaperFileKind.Extra, CoreTypeId = type ?? 0 };
                    Fill(file, upload);
                    files.Add(file);
                }
            }

            return changed;
        }

        private static void SetFile(ICollection<PastPaperFile> files, long paperId, PastPaperFileKind kind, UploadedFile upload)
        {
            var file = files.FirstOrDefault(t => t.Kind == kind);
            if (file is null)
            {
                file = new() { PastPaperId = paperId, Kind = kind };
                files.Add(file);
            }

            Fill(file, upload);
        }

        private static void Fill(PastPaperFile file, UploadedFile upload)
        {
            file.Path = upload.StoredPath;
            file.Extension = upload.FileExtension;
            file.Size = upload.Size;
            file.PageCount = upload.Pages;
            file.CreationDate = DateTimeOffset.UtcNow;
            file.CheckDate = DateTimeOffset.UtcNow;
        }

        /// <summary>An upload key's file, when it is the caller's, still valid, and of an allowed kind; otherwise none (as
        /// gama-api ignores a file it can't find).</summary>
        private UploadedFile? ReadUpload(string? key, long userId, string[] extensions)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                var file = JsonSerializer.Deserialize<UploadedFile>(dataProtectionProvider.Value.CreateProtector(UploadPurpose).ToTimeLimitedDataProtector().Unprotect(key));
                return file is not null && file.UserId == userId && extensions.Contains(file.FileExtension) ? file : null;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
            catch (Exception exc) when (exc is JsonException or FormatException)
            {
                return null;
            }
        }

        /// <summary>Saves a new paper under the next id after the highest one (and its new extra files likewise), trying again
        /// if another add took the same id meanwhile.</summary>
        private async Task<long> SaveNewAsync(PastPaper paper)
        {
            for (var attempt = 1; attempt <= SaveAttempts; attempt++)
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                paper.Id = (await uow.GetRepository<PastPaper>().GetManyQueryable(t => true).MaxAsync(t => (long?)t.Id) ?? 0) + 1;
                foreach (var file in paper.Files!)
                {
                    file.PastPaperId = paper.Id;
                    file.CoreExtraFileId = null;
                }

                await AssignExtraIdsAsync(uow, paper);
                uow.GetRepository<PastPaper>().Add(paper);
                try
                {
                    _ = await uow.SaveChangesAsync();
                    return paper.Id;
                }
                catch (DbUpdateException exc) when (attempt < SaveAttempts)
                {
                    Logger.Value.LogWarning(exc, "Past paper {Id} was taken meanwhile; trying the next id", paper.Id);
                }
            }

            throw new InvalidOperationException("No free past paper id.");
        }

        /// <summary>New extra files get the next extra file id (downloads name an extra file by it).</summary>
        private static async Task AssignExtraIdsAsync(IUnitOfWork uow, PastPaper paper)
        {
            var added = paper.Files?.Where(t => t.Kind == PastPaperFileKind.Extra && t.CoreExtraFileId is null).ToList() ?? [];
            if (added.Count == 0)
            {
                return;
            }

            var next = (await uow.GetRepository<PastPaperFile>().GetManyQueryable(t => t.CoreExtraFileId != null).MaxAsync(t => t.CoreExtraFileId) ?? 0) + 1;
            foreach (var file in added)
            {
                file.CoreExtraFileId = next++;
            }
        }

        private static short? PageCount(byte[] pdf)
        {
            if (!(OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            {
                return null;
            }

            try
            {
                var count = PDFtoImage.Conversion.GetPageCount(pdf);
                return count is > 0 and <= short.MaxValue ? (short)count : null;
            }
            catch (Exception exc) when (exc is PDFtoImage.Exceptions.PdfException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The question PDF's first page as the paper's picture (gama-api's <c>pdfToPNG</c>), in the public thumbnail
        /// container. None when the PDF can't be read.</summary>
        private async Task<string?> ThumbnailAsync(byte[] pdf)
        {
            if (!(OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            {
                return null;
            }

            try
            {
                using var bitmap = PDFtoImage.Conversion.ToImage(pdf, 0, null, new() { Width = ThumbnailWidth, WithAspectRatio = true });
                using var image = bitmap.Encode(SKEncodedImageFormat.Webp, 80);
                return await StoreAsync(image.ToArray(), "thumbnail.webp", "image/webp", ContainerType.Thumbnail);
            }
            catch (Exception exc) when (exc is PDFtoImage.Exceptions.PdfException or ArgumentException)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        private async Task<string?> StoreAsync(byte[] content, string fileName, string contentType, ContainerType container)
        {
            using System.IO.MemoryStream stream = new(content);
            FormFile file = new(stream, 0, content.LongLength, "file", fileName) { Headers = new HeaderDictionary(), ContentType = contentType };
            var stored = await fileService.Value.CreateFileAsync(new() { File = file, ContainerType = container });
            return stored.OperationResult is OperationResult.Succeeded ? stored.Data : null;
        }

        // ---- Helpers ----

        private static Task<bool> HasRoleAsync(IUnitOfWork uow, long userId, params string[] roles) =>
            uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId && t.UserRoles!.Any(r => roles.Contains(r.Role!.Name!))).AnyAsync();

        /// <summary>A field as text (a string, a number or a boolean), or null when it is missing or empty.</summary>
        private static string? Field(JsonObject? body, string key) => Field(body?[key]);

        private static string? Field(JsonNode? node) => (node as JsonValue)?.GetValueKind() switch
        {
            JsonValueKind.String => NullIfEmpty(node.GetValue<string>().Trim()),
            JsonValueKind.Number => node.ToJsonString(),
            JsonValueKind.True => "1",
            _ => null,
        };

        private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

        /// <summary>PHP's <c>empty()</c> on a request field.</summary>
        private static bool IsEmpty(JsonNode? node) => node switch
        {
            null => true,
            JsonArray array => array.Count == 0,
            JsonObject => false,
            _ => Field(node) is null or "0",
        };

        private static ResultData<JsonNode> Refuse(string code, string? reason, string message) =>
            new(OperationResult.Failed) { Errors = [new() { Message = message, Reference = code, Info = reason }] };

        private static ResultData<JsonNode> Required(IReadOnlyCollection<string> fields) =>
            new(OperationResult.NotValid) { Errors = [new() { Message = $"Required: {string.Join(", ", fields)}.", Reference = "requiredField", Info = string.Join(',', fields) }] };

        /// <summary>gama-api's answer type check: sent, and 0, 1 or 2.</summary>
        private static bool IsAnswerType(string? value) =>
            int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n <= 2;

        [System.Text.RegularExpressions.GeneratedRegex("<[^>]*>")]
        private static partial System.Text.RegularExpressions.Regex TagRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"<(?!/?(b|br|p)\b)[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex NonBasicTagRegex();
    }
}
