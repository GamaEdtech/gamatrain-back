using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <summary>
    /// Generalizes ExamExportPurchases into ContentPurchases (any pay-once content, see PurchasableContentType), keeping
    /// every existing row: rename the table and ExamId -> ContentId, add ContentType (all existing rows are ExamExport = 1)
    /// and Variant (from the old FileType: 0 Pdf, 1 Word, 2 PowerPoint -- ExportFileType's values), then drop FileType
    /// and swap the unique index. Hand-written: EF scaffolded a drop-and-recreate, which would lose the purchases.
    /// </summary>
    public partial class GeneralizeContentPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_ExamExportPurchases_ApplicationUsers_UserId", table: "ExamExportPurchases");
            migrationBuilder.DropIndex(name: "IX_ExamExportPurchases_UserId_ExamId_FileType", table: "ExamExportPurchases");
            migrationBuilder.DropPrimaryKey(name: "PK_ExamExportPurchases", table: "ExamExportPurchases");

            migrationBuilder.RenameTable(name: "ExamExportPurchases", newName: "ContentPurchases");
            migrationBuilder.RenameColumn(name: "ExamId", table: "ContentPurchases", newName: "ContentId");

            // Added nullable, backfilled, then made NOT NULL -- so no default constraint is left behind.
            migrationBuilder.AddColumn<byte>(name: "ContentType", table: "ContentPurchases", type: "tinyint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Variant", table: "ContentPurchases", type: "nvarchar(50)", maxLength: 50, nullable: true);
            migrationBuilder.Sql(
                "UPDATE ContentPurchases SET ContentType = 1, " +
                "Variant = CASE FileType WHEN 0 THEN N'Pdf' WHEN 1 THEN N'Word' WHEN 2 THEN N'PowerPoint' ELSE N'' END");
            migrationBuilder.AlterColumn<byte>(name: "ContentType", table: "ContentPurchases", type: "tinyint", nullable: false, oldClrType: typeof(byte), oldType: "tinyint", oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "Variant", table: "ContentPurchases", type: "nvarchar(50)", maxLength: 50, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(50)", oldMaxLength: 50, oldNullable: true);
            migrationBuilder.DropColumn(name: "FileType", table: "ContentPurchases");

            migrationBuilder.AddPrimaryKey(name: "PK_ContentPurchases", table: "ContentPurchases", column: "Id");
            migrationBuilder.CreateIndex(
                name: "IX_ContentPurchases_UserId_ContentType_ContentId_Variant",
                table: "ContentPurchases",
                columns: new[] { "UserId", "ContentType", "ContentId", "Variant" },
                unique: true);
            migrationBuilder.AddForeignKey(
                name: "FK_ContentPurchases_ApplicationUsers_UserId",
                table: "ContentPurchases",
                column: "UserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only ExamExport rows fit the old table; other kinds of purchases are dropped.
            migrationBuilder.DropForeignKey(name: "FK_ContentPurchases_ApplicationUsers_UserId", table: "ContentPurchases");
            migrationBuilder.DropIndex(name: "IX_ContentPurchases_UserId_ContentType_ContentId_Variant", table: "ContentPurchases");
            migrationBuilder.DropPrimaryKey(name: "PK_ContentPurchases", table: "ContentPurchases");
            migrationBuilder.Sql("DELETE FROM ContentPurchases WHERE ContentType <> 1");

            migrationBuilder.AddColumn<byte>(name: "FileType", table: "ContentPurchases", type: "tinyint", nullable: true);
            migrationBuilder.Sql("UPDATE ContentPurchases SET FileType = CASE Variant WHEN N'Pdf' THEN 0 WHEN N'Word' THEN 1 WHEN N'PowerPoint' THEN 2 ELSE 0 END");
            migrationBuilder.AlterColumn<byte>(name: "FileType", table: "ContentPurchases", type: "tinyint", nullable: false, oldClrType: typeof(byte), oldType: "tinyint", oldNullable: true);
            migrationBuilder.DropColumn(name: "Variant", table: "ContentPurchases");
            migrationBuilder.DropColumn(name: "ContentType", table: "ContentPurchases");
            migrationBuilder.RenameColumn(name: "ContentId", table: "ContentPurchases", newName: "ExamId");
            migrationBuilder.RenameTable(name: "ContentPurchases", newName: "ExamExportPurchases");

            migrationBuilder.AddPrimaryKey(name: "PK_ExamExportPurchases", table: "ExamExportPurchases", column: "Id");
            migrationBuilder.CreateIndex(
                name: "IX_ExamExportPurchases_UserId_ExamId_FileType",
                table: "ExamExportPurchases",
                columns: new[] { "UserId", "ExamId", "FileType" },
                unique: true);
            migrationBuilder.AddForeignKey(
                name: "FK_ExamExportPurchases_ApplicationUsers_UserId",
                table: "ExamExportPurchases",
                column: "UserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id");
        }
    }
}
