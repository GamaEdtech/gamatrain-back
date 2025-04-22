using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.SqlServer.Types;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFaq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ContributionId",
                table: "SchoolImages",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Faq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SummaryOfQuestion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faq", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FaqCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CategoryType = table.Column<int>(type: "int", nullable: false),
                    HierarchyPath = table.Column<SqlHierarchyId>(type: "hierarchyid", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaqCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MediaType = table.Column<int>(type: "int", nullable: false),
                    MediaEntity = table.Column<int>(type: "int", nullable: false),
                    MediaEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaqId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Media_Faq_FaqId",
                        column: x => x.FaqId,
                        principalTable: "Faq",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FaqAndFaqCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaqId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaqCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaqAndFaqCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaqAndFaqCategory_FaqCategory_FaqCategoryId",
                        column: x => x.FaqCategoryId,
                        principalTable: "FaqCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaqAndFaqCategory_Faq_FaqId",
                        column: x => x.FaqId,
                        principalTable: "Faq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolTags_SchoolId_TagId",
                table: "SchoolTags",
                columns: new[] { "SchoolId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolImages_ContributionId",
                table: "SchoolImages",
                column: "ContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_PostTags_PostId_TagId",
                table: "PostTags",
                columns: new[] { "PostId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaqAndFaqCategory_FaqCategoryId",
                table: "FaqAndFaqCategory",
                column: "FaqCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FaqAndFaqCategory_FaqId",
                table: "FaqAndFaqCategory",
                column: "FaqId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_FaqId",
                table: "Media",
                column: "FaqId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_MediaEntity_MediaEntityId",
                table: "Media",
                columns: new[] { "MediaEntity", "MediaEntityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SchoolImages_Contributions_ContributionId",
                table: "SchoolImages",
                column: "ContributionId",
                principalTable: "Contributions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SchoolImages_Contributions_ContributionId",
                table: "SchoolImages");

            migrationBuilder.DropTable(
                name: "FaqAndFaqCategory");

            migrationBuilder.DropTable(
                name: "Media");

            migrationBuilder.DropTable(
                name: "FaqCategory");

            migrationBuilder.DropTable(
                name: "Faq");

            migrationBuilder.DropIndex(
                name: "IX_SchoolTags_SchoolId_TagId",
                table: "SchoolTags");

            migrationBuilder.DropIndex(
                name: "IX_SchoolImages_ContributionId",
                table: "SchoolImages");

            migrationBuilder.DropIndex(
                name: "IX_PostTags_PostId_TagId",
                table: "PostTags");

            migrationBuilder.DropColumn(
                name: "ContributionId",
                table: "SchoolImages");

            migrationBuilder.InsertData(
                table: "ApplicationRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 1, "85465B3B-E646-49BC-AAC6-D07C450B3AE3", "Admin", "ADMIN" },
                    { 2, "85465B3B-E646-49BC-AAC6-D07C450B3AE4", "Teacher", "TEACHER" },
                    { 3, "85465B3B-E646-49BC-AAC6-D07C450B3AE5", "Student", "STUDENT" }
                });

            migrationBuilder.InsertData(
                table: "ApplicationUsers",
                columns: new[] { "Id", "AccessFailedCount", "Avatar", "ConcurrencyStamp", "Email", "EmailConfirmed", "Enabled", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "RegistrationDate", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { 1, 0, null, "5BABA139-4AE5-4C47-BC65-DE4849346A17", "admin@gamaedtech.com", true, true, null, null, false, null, "ADMIN@GAMAEDTECH.COM", "ADMIN", "AQAAAAIAAYagAAAAEMLN3xqYWUja6ShSK0teeCYzziU6b+KghL4AiSXrb03Y3VbBfxKP7LUF3PZAJhQJ+Q==", "09355028981", true, new DateTimeOffset(new DateTime(2023, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "EAF1FA85-3DA1-4A40-90C6-65B97BF903F1", false, "admin" });

            migrationBuilder.InsertData(
                table: "ApplicationUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[] { 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolTags_SchoolId",
                table: "SchoolTags",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PostTags_PostId",
                table: "PostTags",
                column: "PostId");
        }
    }
}
