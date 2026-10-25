using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Rankeo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Salas",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "char(6)", nullable: false),
                    Estado = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Salas", x => x.Codigo);
                    table.CheckConstraint("CK_Salas_Codigo", "\"Codigo\" ~ '^[A-HJ-NP-Z2-9]{6}$'");
                });

            migrationBuilder.CreateTable(
                name: "Bitacora",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalaCodigo = table.Column<string>(type: "char(6)", nullable: false),
                    Quien = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Color = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Texto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    En = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bitacora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bitacora_Salas_SalaCodigo",
                        column: x => x.SalaCodigo,
                        principalTable: "Salas",
                        principalColumn: "Codigo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Imagenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalaCodigo = table.Column<string>(type: "char(6)", nullable: false),
                    Archivo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Bytes = table.Column<int>(type: "integer", nullable: false),
                    SubidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Imagenes", x => x.Id);
                    table.CheckConstraint("CK_Imagenes_Bytes", "\"Bytes\" > 0");
                    table.ForeignKey(
                        name: "FK_Imagenes_Salas_SalaCodigo",
                        column: x => x.SalaCodigo,
                        principalTable: "Salas",
                        principalColumn: "Codigo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_SalaCodigo_Id",
                table: "Bitacora",
                columns: new[] { "SalaCodigo", "Id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Imagenes_SalaCodigo",
                table: "Imagenes",
                column: "SalaCodigo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bitacora");

            migrationBuilder.DropTable(
                name: "Imagenes");

            migrationBuilder.DropTable(
                name: "Salas");
        }
    }
}
