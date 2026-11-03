using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ListagemPedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_ClienteId_CriadoEm",
                table: "Pedidos");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId_CriadoEm_Numero",
                table: "Pedidos",
                columns: new[] { "ClienteId", "CriadoEm", "Numero" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Status", "AtualizadoEm", "ValorTotal", "Moeda" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoEm_Numero",
                table: "Pedidos",
                columns: new[] { "CriadoEm", "Numero" },
                descending: new bool[0])
                .Annotation("SqlServer:Include", new[] { "ClienteId", "Status", "AtualizadoEm", "ValorTotal", "Moeda" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_ClienteId_CriadoEm_Numero",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_CriadoEm_Numero",
                table: "Pedidos");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId_CriadoEm",
                table: "Pedidos",
                columns: new[] { "ClienteId", "CriadoEm" });
        }
    }
}
