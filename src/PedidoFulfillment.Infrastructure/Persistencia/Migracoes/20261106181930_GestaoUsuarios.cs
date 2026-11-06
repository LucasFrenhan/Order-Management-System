using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class GestaoUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DesativadoEm",
                table: "Usuarios",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReativadoEm",
                table: "Usuarios",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Nome_Id",
                table: "Usuarios",
                columns: new[] { "Nome", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usuarios_Nome_Id",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "DesativadoEm",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "ReativadoEm",
                table: "Usuarios");
        }
    }
}
