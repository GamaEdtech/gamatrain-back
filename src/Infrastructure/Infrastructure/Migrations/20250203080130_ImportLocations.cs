using System.Text;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamaEdtech.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImportLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Locations_Title_Type_ParentId",
                table: "Locations");

            // The script contains ~156k single-line INSERT statements. Executing them as one
            // batch exhausts SQL Server's query compile memory (error 701) on constrained
            const int batchSize = 1000;
            const string lineSeparator = ";";
            var lines = Resources.Locations.List.Split(lineSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var chunks = lines.Chunk(batchSize);
            foreach (var chunk in chunks)
            {
                migrationBuilder.Sql(string.Join(lineSeparator + Environment.NewLine, chunk));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Locations_Title_Type_ParentId",
                table: "Locations",
                columns: new[] { "Title", "Type", "ParentId" },
                unique: true,
                filter: "[ParentId] IS NOT NULL");
        }
    }
}
