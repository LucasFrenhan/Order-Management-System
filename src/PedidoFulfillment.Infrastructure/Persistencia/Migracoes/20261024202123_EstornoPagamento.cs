using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class EstornoPagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstornadoEm",
                table: "Pagamentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstornoSolicitadoEm",
                table: "Pagamentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificadorEstorno",
                table: "Pagamentos",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasEstorno",
                table: "Pagamentos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UltimaFalhaEstornoEm",
                table: "Pagamentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CanceladaEm",
                table: "OrdensEntrega",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_EstornoPendente",
                table: "Pagamentos",
                columns: new[] { "UltimaFalhaEstornoEm", "EstornoSolicitadoEm" },
                filter: "[Situacao] = N'EstornoPendente'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos",
                sql: "[Situacao] IN (N'Pendente', N'Aprovado', N'Recusado', N'NaoProcessado', N'EstornoPendente', N'Estornado')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_TentativasEstornoNaoNegativas",
                table: "Pagamentos",
                sql: "[TentativasEstorno] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pagamentos_EstornoPendente",
                table: "Pagamentos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_TentativasEstornoNaoNegativas",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "EstornadoEm",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "EstornoSolicitadoEm",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "IdentificadorEstorno",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "TentativasEstorno",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "UltimaFalhaEstornoEm",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "CanceladaEm",
                table: "OrdensEntrega");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos",
                sql: "[Situacao] IN (N'Pendente', N'Aprovado', N'Recusado', N'NaoProcessado')");
        }
    }
}
