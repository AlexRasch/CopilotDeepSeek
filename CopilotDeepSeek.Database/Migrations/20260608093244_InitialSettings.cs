using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CopilotDeepSeek.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsAllowed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AutoRun = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5000),
                    DefaultModelId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxMessages = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    BalanceRefreshIntervalSec = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 60)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "ApiKey", "AutoRun", "BalanceRefreshIntervalSec", "BaseUrl", "DefaultModelId", "Port" },
                values: new object[] { 1, "", true, 60, "https://api.deepseek.com", null, 5000 });

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_Name",
                table: "AiModels",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiModels");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
