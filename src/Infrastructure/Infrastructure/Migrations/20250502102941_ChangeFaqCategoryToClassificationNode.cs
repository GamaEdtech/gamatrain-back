using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.SqlServer.Types;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeFaqCategoryToClassificationNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaqAndFaqCategory");

            migrationBuilder.DropTable(
                name: "FaqCategory");

            migrationBuilder.CreateTable(
                name: "ClassificationNode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Image = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowMultipleSelection = table.Column<bool>(type: "bit", nullable: false),
                    NodeType = table.Column<int>(type: "int", nullable: false),
                    HierarchyPath = table.Column<SqlHierarchyId>(type: "hierarchyid", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationNode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationNodeRelationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeRelationshipEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassificationNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeRelationEntityType = table.Column<int>(type: "int", nullable: false),
                    FaqId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationNodeRelationship", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationNodeRelationship_ClassificationNode_ClassificationNodeId",
                        column: x => x.ClassificationNodeId,
                        principalTable: "ClassificationNode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassificationNodeRelationship_Faq_FaqId",
                        column: x => x.FaqId,
                        principalTable: "Faq",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationNodeRelationship_ClassificationNodeId",
                table: "ClassificationNodeRelationship",
                column: "ClassificationNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationNodeRelationship_FaqId",
                table: "ClassificationNodeRelationship",
                column: "FaqId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassificationNodeRelationship");

            migrationBuilder.DropTable(
                name: "ClassificationNode");

            migrationBuilder.CreateTable(
                name: "FaqCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FaqCategoryType = table.Column<int>(type: "int", nullable: false),
                    HierarchyPath = table.Column<SqlHierarchyId>(type: "hierarchyid", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaqCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FaqAndFaqCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaqCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaqId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoftDeleted = table.Column<bool>(type: "bit", nullable: false)
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
                name: "IX_FaqAndFaqCategory_FaqCategoryId",
                table: "FaqAndFaqCategory",
                column: "FaqCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FaqAndFaqCategory_FaqId",
                table: "FaqAndFaqCategory",
                column: "FaqId");
        }
    }
}
