using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class LimiteRequisicoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContagensLimite",
                columns: table => new
                {
                    Chave = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    Contador = table.Column<int>(type: "int", nullable: false),
                    FimJanela = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    BloqueioRegistrado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContagensLimite", x => x.Chave);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContagensLimite_FimJanela",
                table: "ContagensLimite",
                column: "FimJanela");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContagensLimite");
        }
    }
}
