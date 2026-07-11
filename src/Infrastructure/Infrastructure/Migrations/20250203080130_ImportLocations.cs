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

            var bytes = (byte[])Resource1.ResourceManager.GetObject("Locations");
            var sql = UTF8Encoding.UTF8.GetString(bytes);

            // The script contains ~156k single-line INSERT statements. Executing them as one
            // batch exhausts SQL Server's query compile memory (error 701) on constrained
            // instances, so run the script in smaller batches. All batches share the migration's
            // connection, so the leading SET IDENTITY_INSERT ON stays in effect until the
            // trailing OFF.
            const int batchSize = 1000;
            var lines = sql.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < lines.Length; i += batchSize)
            {
                migrationBuilder.Sql(string.Join(Environment.NewLine, lines[i..Math.Min(i + batchSize, lines.Length)]));
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
