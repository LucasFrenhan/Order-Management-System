using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class LimpezaEReenvioDlq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PedidosReenvioDlq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumidor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TipoEvento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FalhouDe = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FalhouAte = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TrechoMotivo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Situacao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RegistradoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IniciadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcluidoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reenviadas = table.Column<int>(type: "int", nullable: false),
                    BloqueadasPeloLimite = table.Column<int>(type: "int", nullable: false),
                    ForaDoFiltro = table.Column<int>(type: "int", nullable: false),
                    MotivoFalha = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidosReenvioDlq", x => x.Id);
                    table.CheckConstraint("CK_PedidosReenvioDlq_ContagensNaoNegativas", "[Reenviadas] >= 0 AND [BloqueadasPeloLimite] >= 0 AND [ForaDoFiltro] >= 0");
                    table.CheckConstraint("CK_PedidosReenvioDlq_SituacaoValida", "[Situacao] IN (N'Pendente', N'EmExecucao', N'Concluido', N'Falhou')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosSaida_Enviados",
                table: "EventosSaida",
                column: "EnviadoEm",
                filter: "[EnviadoEm] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EventosProcessados_ProcessadoEm",
                table: "EventosProcessados",
                column: "ProcessadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosReenvioDlq_Situacao_RegistradoEm",
                table: "PedidosReenvioDlq",
                columns: new[] { "Situacao", "RegistradoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PedidosReenvioDlq");

            migrationBuilder.DropIndex(
                name: "IX_EventosSaida_Enviados",
                table: "EventosSaida");

            migrationBuilder.DropIndex(
                name: "IX_EventosProcessados_ProcessadoEm",
                table: "EventosProcessados");
        }
    }
}
