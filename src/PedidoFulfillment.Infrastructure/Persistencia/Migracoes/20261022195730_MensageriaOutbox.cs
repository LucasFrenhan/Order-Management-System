using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class MensageriaOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosProcessados",
                columns: table => new
                {
                    Consumidor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdEvento = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosProcessados", x => new { x.Consumidor, x.IdEvento });
                });

            migrationBuilder.CreateTable(
                name: "EventosSaida",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdEvento = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Conteudo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EnviadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UltimoErro = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosSaida", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosSaida_IdEvento",
                table: "EventosSaida",
                column: "IdEvento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosSaida_Pendentes",
                table: "EventosSaida",
                columns: new[] { "ProximaTentativaEm", "Id" },
                filter: "[EnviadoEm] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosProcessados");

            migrationBuilder.DropTable(
                name: "EventosSaida");
        }
    }
}
