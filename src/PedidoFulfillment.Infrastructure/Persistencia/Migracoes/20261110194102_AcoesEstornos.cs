using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AcoesEstornos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstornoAtrasadoEm",
                table: "Pagamentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstornoBaixadoEm",
                table: "Pagamentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EstornoBaixadoPorId",
                table: "Pagamentos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoBaixaEstorno",
                table: "Pagamentos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenciaExternaBaixaEstorno",
                table: "Pagamentos",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TentativaEstornoForcadaEm",
                table: "Pagamentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UltimoErroEstorno",
                table: "Pagamentos",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_EstornoForcado",
                table: "Pagamentos",
                column: "TentativaEstornoForcadaEm",
                filter: "[Situacao] = N'EstornoPendente' AND [TentativaEstornoForcadaEm] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_EstornoPendenteListagem",
                table: "Pagamentos",
                columns: new[] { "EstornoSolicitadoEm", "Id" },
                filter: "[Situacao] = N'EstornoPendente'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_BaixaCompleta",
                table: "Pagamentos",
                sql: "([Situacao] = N'EstornoBaixado' AND [EstornoBaixadoEm] IS NOT NULL AND [EstornoBaixadoPorId] IS NOT NULL AND [MotivoBaixaEstorno] IS NOT NULL) OR ([Situacao] <> N'EstornoBaixado' AND [EstornoBaixadoEm] IS NULL AND [EstornoBaixadoPorId] IS NULL AND [MotivoBaixaEstorno] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos",
                sql: "[Situacao] IN (N'Pendente', N'Aprovado', N'Recusado', N'NaoProcessado', N'EstornoPendente', N'Estornado', N'EstornoBaixado')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pagamentos_EstornoForcado",
                table: "Pagamentos");

            migrationBuilder.DropIndex(
                name: "IX_Pagamentos_EstornoPendenteListagem",
                table: "Pagamentos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_BaixaCompleta",
                table: "Pagamentos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "EstornoAtrasadoEm",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "EstornoBaixadoEm",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "EstornoBaixadoPorId",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "MotivoBaixaEstorno",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "ReferenciaExternaBaixaEstorno",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "TentativaEstornoForcadaEm",
                table: "Pagamentos");

            migrationBuilder.DropColumn(
                name: "UltimoErroEstorno",
                table: "Pagamentos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pagamentos_SituacaoValida",
                table: "Pagamentos",
                sql: "[Situacao] IN (N'Pendente', N'Aprovado', N'Recusado', N'NaoProcessado', N'EstornoPendente', N'Estornado')");
        }
    }
}
