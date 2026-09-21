using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusToPostAndPostComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionComment",
                table: "Posts",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "Posts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)2);

            migrationBuilder.AddColumn<string>(
                name: "RejectionComment",
                table: "PostComments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "PostComments",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)2);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Status",
                table: "Posts",
                column: "Status");

            // Existing rows are all live content, hence the Confirmed (2) column default above. What follows folds the
            // pending blog-post Contributions into the new model without deleting anything: every Contribution row is left
            // untouched as a historical record. Only Post (4) contributions that don't yet map to a live Post
            // (IdentifierId IS NULL) and are still Draft (0) / Review (1) / Rejected (3) become Post rows carrying the
            // same status. Pending *edits* of an already-live post (IdentifierId set) are not migrated - a Post now has
            // a single row, so there is nowhere to hold a second, unpublished version.
            migrationBuilder.Sql("""
DECLARE @map TABLE (ContributionId bigint NOT NULL, PostId bigint NOT NULL);

;WITH src AS
(
    SELECT c.Id AS ContributionId, c.CreationUserId, c.CreationDate, c.Status, c.Comment,
        LEFT(ISNULL(JSON_VALUE(c.Data, '$.Title'), N''), 500) AS Title,
        LEFT(ISNULL(JSON_VALUE(c.Data, '$.Summary'), N''), 2000) AS Summary,
        ISNULL(JSON_VALUE(c.Data, '$.Body'), N'') AS Body,
        LEFT(ISNULL(JSON_VALUE(c.Data, '$.ImageId'), N''), 100) AS ImageId,
        LEFT(JSON_VALUE(c.Data, '$.PodcastId'), 100) AS PodcastId,
        LEFT(JSON_VALUE(c.Data, '$.Keywords'), 500) AS Keywords,
        ISNULL(TRY_CAST(JSON_VALUE(c.Data, '$.PublishDate') AS datetimeoffset), c.CreationDate) AS PublishDate,
        ISNULL(TRY_CAST(ISNULL(JSON_VALUE(c.Data, '$.VisibilityType.Value'),
            CASE JSON_VALUE(c.Data, '$.VisibilityType.Name') WHEN 'General' THEN '0' WHEN 'Premium' THEN '1' WHEN 'Private' THEN '2' END) AS tinyint), 2) AS VisibilityType,
        NULLIF(LEFT(JSON_VALUE(c.Data, '$.Slug'), 480), N'') AS RawSlug
    FROM Contributions c
    WHERE c.CategoryType = 4 AND c.IdentifierId IS NULL AND c.Status IN (0, 1, 3) AND c.Data IS NOT NULL
),
slugged AS
(
    SELECT src.*,
        ROW_NUMBER() OVER (PARTITION BY src.RawSlug ORDER BY src.ContributionId) AS SlugRank
    FROM src
)
MERGE INTO Posts AS p
USING
(
    SELECT s.*,
        CASE
            WHEN s.RawSlug IS NULL THEN N'contribution-' + CAST(s.ContributionId AS nvarchar(20))
            WHEN s.SlugRank > 1 OR EXISTS (SELECT 1 FROM Posts e WHERE e.Slug = s.RawSlug) THEN s.RawSlug + N'-' + CAST(s.ContributionId AS nvarchar(20))
            ELSE s.RawSlug
        END AS Slug
    FROM slugged s
) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (Slug, Title, Summary, Body, ImageId, PodcastId, LikeCount, DislikeCount, PublishDate, VisibilityType, Keywords, ViewCount, Status, RejectionComment, CreationUserId, CreationDate)
    VALUES (s.Slug, s.Title, s.Summary, s.Body, s.ImageId, s.PodcastId, 0, 0, s.PublishDate, s.VisibilityType, s.Keywords, 0, s.Status, s.Comment, s.CreationUserId, s.CreationDate)
OUTPUT s.ContributionId, inserted.Id INTO @map (ContributionId, PostId);

INSERT INTO PostTags (PostId, TagId, CreationUserId, CreationDate)
SELECT DISTINCT m.PostId, t.Id, c.CreationUserId, c.CreationDate
FROM @map m
JOIN Contributions c ON c.Id = m.ContributionId
CROSS APPLY OPENJSON(c.Data, '$.Tags') j
JOIN Tags t ON t.Id = TRY_CAST(j.value AS bigint);

INSERT INTO ContentLocalizations (ContentId, ContentType, Name, Value, LanguageId, CreationUserId, CreationDate)
SELECT m.PostId, N'Post', v.Name, v.Value, l.LanguageId, c.CreationUserId, c.CreationDate
FROM @map m
JOIN Contributions c ON c.Id = m.ContributionId
CROSS APPLY OPENJSON(c.Data, '$.LocalizedValues') WITH (LanguageId int '$.LanguageId', Title nvarchar(max) '$.Title', Summary nvarchar(max) '$.Summary', Body nvarchar(max) '$.Body') l
JOIN Languages lg ON lg.Id = l.LanguageId
CROSS APPLY (VALUES (N'Title', l.Title), (N'Summary', l.Summary), (N'Body', l.Body)) v (Name, Value)
WHERE v.Value IS NOT NULL;

-- Pending / rejected comments: PostComments is unique per (CreationUserId, PostId), so only the latest pending or
-- rejected contribution per user+post is carried over, and only if that user has no comment on the post yet.
INSERT INTO PostComments (PostId, Comment, LikeCount, DislikeCount, Status, RejectionComment, CreationUserId, CreationDate)
SELECT r.IdentifierId, r.Comment, 0, 0, r.Status, r.RejectionComment, r.CreationUserId, r.CreationDate
FROM
(
    SELECT c.IdentifierId, JSON_VALUE(c.Data, '$.Comment') AS Comment, c.Status, c.Comment AS RejectionComment, c.CreationUserId, c.CreationDate,
        ROW_NUMBER() OVER (PARTITION BY c.CreationUserId, c.IdentifierId ORDER BY c.Id DESC) AS Rn
    FROM Contributions c
    WHERE c.CategoryType = 7 AND c.IdentifierId IS NOT NULL AND c.Status IN (0, 1, 3) AND c.Data IS NOT NULL
) r
WHERE r.Rn = 1
    AND EXISTS (SELECT 1 FROM Posts p WHERE p.Id = r.IdentifierId)
    AND NOT EXISTS (SELECT 1 FROM PostComments pc WHERE pc.PostId = r.IdentifierId AND pc.CreationUserId = r.CreationUserId);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_Status",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "RejectionComment",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "RejectionComment",
                table: "PostComments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PostComments");
        }
    }
}
