using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class PagamentoNaoProcessado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos",
                sql: "[Situacao] IN (N'Pendente', N'Aprovado', N'Recusado', N'NaoProcessado')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos",
                sql: "[Situacao] IN (N'Pendente', N'Aprovado', N'Recusado')");
        }
    }
}
