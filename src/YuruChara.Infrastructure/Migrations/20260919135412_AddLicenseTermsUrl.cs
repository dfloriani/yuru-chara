using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuruChara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseTermsUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LicenseTermsUrl",
                table: "mascots",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenseTermsUrl",
                table: "mascots");
        }
    }
}
