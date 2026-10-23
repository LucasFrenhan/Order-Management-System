using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ReservaEstoque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EstoqueReservado",
                table: "Pedidos",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstoqueReservado",
                table: "Pedidos");
        }
    }
}
