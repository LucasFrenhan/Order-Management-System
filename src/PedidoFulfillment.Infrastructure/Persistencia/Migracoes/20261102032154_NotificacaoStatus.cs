using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class NotificacaoStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvisosPedido",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdNotificacao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    PedidoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Conteudo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ContextoRastreio = table.Column<string>(type: "varchar(55)", unicode: false, maxLength: 55, nullable: true),
                    Posicao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosPedido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvisosPedido_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CursorNotificacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Posicao = table.Column<byte[]>(type: "binary(8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CursorNotificacoes", x => x.Id);
                    table.CheckConstraint("CK_CursorNotificacoes_LinhaUnica", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "DestinosNotificacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CriadoPor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SegredoCifrado = table.Column<byte[]>(type: "varbinary(60)", maxLength: 60, nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    MotivoDesativacao = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DesativadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FalhasSeguidas = table.Column<int>(type: "int", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Versao = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinosNotificacao", x => x.Id);
                    table.CheckConstraint("CK_DestinosNotificacao_FalhasNaoNegativas", "[FalhasSeguidas] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "EntregasNotificacao",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DestinoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AvisoId = table.Column<long>(type: "bigint", nullable: false),
                    Situacao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PrimeiraTentativaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UltimoCodigoHttp = table.Column<int>(type: "int", nullable: true),
                    UltimoErro = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CriadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinalizadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContextoRastreio = table.Column<string>(type: "varchar(55)", unicode: false, maxLength: 55, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntregasNotificacao", x => x.Id);
                    table.CheckConstraint("CK_EntregasNotificacao_SituacaoValida", "[Situacao] IN (N'Pendente', N'Entregue', N'Esgotada', N'Descartada')");
                    table.CheckConstraint("CK_EntregasNotificacao_TentativasNaoNegativas", "[Tentativas] >= 0");
                    table.ForeignKey(
                        name: "FK_EntregasNotificacao_AvisosPedido_AvisoId",
                        column: x => x.AvisoId,
                        principalTable: "AvisosPedido",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntregasNotificacao_DestinosNotificacao_DestinoId",
                        column: x => x.DestinoId,
                        principalTable: "DestinosNotificacao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "CursorNotificacoes",
                columns: new[] { "Id", "Posicao" },
                values: new object[] { 1, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 } });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosPedido_ClienteId_Posicao",
                table: "AvisosPedido",
                columns: new[] { "ClienteId", "Posicao" });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosPedido_CriadoEm",
                table: "AvisosPedido",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosPedido_IdNotificacao",
                table: "AvisosPedido",
                column: "IdNotificacao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvisosPedido_PedidoId_Posicao",
                table: "AvisosPedido",
                columns: new[] { "PedidoId", "Posicao" });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosPedido_Posicao",
                table: "AvisosPedido",
                column: "Posicao",
                unique: true,
                filter: "[Posicao] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DestinosNotificacao_Ativo",
                table: "DestinosNotificacao",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "IX_DestinosNotificacao_ClienteId",
                table: "DestinosNotificacao",
                column: "ClienteId",
                filter: "[ClienteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasNotificacao_AvisoId",
                table: "EntregasNotificacao",
                column: "AvisoId");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasNotificacao_DestinoId_AvisoId",
                table: "EntregasNotificacao",
                columns: new[] { "DestinoId", "AvisoId" },
                unique: true,
                filter: "[DestinoId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasNotificacao_Finalizadas",
                table: "EntregasNotificacao",
                column: "FinalizadaEm",
                filter: "[FinalizadaEm] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntregasNotificacao_Pendentes",
                table: "EntregasNotificacao",
                columns: new[] { "Situacao", "ProximaTentativaEm" },
                filter: "[Situacao] = N'Pendente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CursorNotificacoes");

            migrationBuilder.DropTable(
                name: "EntregasNotificacao");

            migrationBuilder.DropTable(
                name: "AvisosPedido");

            migrationBuilder.DropTable(
                name: "DestinosNotificacao");
        }
    }
}
