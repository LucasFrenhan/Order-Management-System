using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CicloAutenticacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessoesAutenticacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IniciadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EncerradaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessoesAutenticacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessoesAutenticacao_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SolicitacoesEmail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Destino = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProximaTentativaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitacoesEmail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokensUsoUnico",
                columns: table => new
                {
                    HashToken = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Finalidade = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokensUsoUnico", x => x.HashToken);
                    table.ForeignKey(
                        name: "FK_TokensUsoUnico_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TokensRenovacao",
                columns: table => new
                {
                    HashToken = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    SessaoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokensRenovacao", x => x.HashToken);
                    table.ForeignKey(
                        name: "FK_TokensRenovacao_SessoesAutenticacao_SessaoId",
                        column: x => x.SessaoId,
                        principalTable: "SessoesAutenticacao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessoesAutenticacao_ExpiraEm",
                table: "SessoesAutenticacao",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_SessoesAutenticacao_UsuarioId",
                table: "SessoesAutenticacao",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesEmail_ProximaTentativaEm",
                table: "SolicitacoesEmail",
                column: "ProximaTentativaEm");

            migrationBuilder.CreateIndex(
                name: "IX_TokensRenovacao_SessaoId",
                table: "TokensRenovacao",
                column: "SessaoId");

            migrationBuilder.CreateIndex(
                name: "IX_TokensUsoUnico_ExpiraEm",
                table: "TokensUsoUnico",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_TokensUsoUnico_UsuarioId_Finalidade",
                table: "TokensUsoUnico",
                columns: new[] { "UsuarioId", "Finalidade" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SolicitacoesEmail");

            migrationBuilder.DropTable(
                name: "TokensRenovacao");

            migrationBuilder.DropTable(
                name: "TokensUsoUnico");

            migrationBuilder.DropTable(
                name: "SessoesAutenticacao");
        }
    }
}
