using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace YuruChara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "prefectures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameJa = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameRomaji = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Boundary = table.Column<MultiPolygon>(type: "geometry(MultiPolygon, 4326)", nullable: false),
                    LabelPoint = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    Centroid = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prefectures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mascots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrefectureId = table.Column<int>(type: "integer", nullable: false),
                    NameJa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NameRomaji = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Motif = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DebutYear = table.Column<int>(type: "integer", nullable: true),
                    OwningBody = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OfficialUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    ImageLicenseStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LicenseNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    VerificationLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceCitations = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mascots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mascots_prefectures_PrefectureId",
                        column: x => x.PrefectureId,
                        principalTable: "prefectures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mascots_DebutYear",
                table: "mascots",
                column: "DebutYear");

            migrationBuilder.CreateIndex(
                name: "IX_mascots_Motif",
                table: "mascots",
                column: "Motif");

            migrationBuilder.CreateIndex(
                name: "IX_mascots_PrefectureId",
                table: "mascots",
                column: "PrefectureId");

            migrationBuilder.CreateIndex(
                name: "ix_prefectures_boundary_gist",
                table: "prefectures",
                column: "Boundary")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mascots");

            migrationBuilder.DropTable(
                name: "prefectures");
        }
    }
}
