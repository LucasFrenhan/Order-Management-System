using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ContextoRastreio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextoRastreio",
                table: "PedidosReenvioDlq",
                type: "varchar(55)",
                unicode: false,
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextoRastreio",
                table: "Pedidos",
                type: "varchar(55)",
                unicode: false,
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextoRastreio",
                table: "EventosSaida",
                type: "varchar(55)",
                unicode: false,
                maxLength: 55,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextoRastreio",
                table: "PedidosReenvioDlq");

            migrationBuilder.DropColumn(
                name: "ContextoRastreio",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ContextoRastreio",
                table: "EventosSaida");
        }
    }
}
