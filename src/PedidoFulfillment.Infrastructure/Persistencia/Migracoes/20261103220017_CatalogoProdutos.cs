using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PedidoFulfillment.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CatalogoProdutos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextoBusca",
                table: "Produtos",
                type: "nvarchar(1260)",
                maxLength: 1260,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Ativo_Nome_Sku",
                table: "Produtos",
                columns: new[] { "Ativo", "Nome", "Sku" })
                .Annotation("SqlServer:Include", new[] { "Preco", "QuantidadeEstoque" });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Nome_Sku",
                table: "Produtos",
                columns: new[] { "Nome", "Sku" })
                .Annotation("SqlServer:Include", new[] { "Preco", "QuantidadeEstoque", "Ativo" });

            // Produtos que já existiam (catálogo inicial e cadastrados): o mesmo texto do InterceptorTextoBusca.
            migrationBuilder.Sql("UPDATE Produtos SET TextoBusca = RTRIM(CONCAT(Nome, N' ', Sku, N' ', Descricao));");

            // Full-Text Search (REQ-22, research R-01 e R-03): o SQL Server não cria catálogo nem índice
            // full-text dentro de transação. Sem o FTS instalado, a migration para com uma mensagem clara.
            migrationBuilder.Sql(
                "IF CAST(FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS int) <> 1 " +
                "THROW 50001, 'O Full-Text Search do SQL Server é obrigatório (REQ-22); use a imagem docker/sqlserver.', 1;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "CREATE FULLTEXT CATALOG CatalogoProdutos WITH ACCENT_SENSITIVITY = OFF AS DEFAULT;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "CREATE FULLTEXT INDEX ON Produtos (TextoBusca LANGUAGE 1046) KEY INDEX PK_Produtos " +
                "ON CatalogoProdutos WITH CHANGE_TRACKING AUTO, STOPLIST = OFF;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FULLTEXT INDEX ON Produtos;", suppressTransaction: true);
            migrationBuilder.Sql("DROP FULLTEXT CATALOG CatalogoProdutos;", suppressTransaction: true);

            migrationBuilder.DropIndex(
                name: "IX_Produtos_Ativo_Nome_Sku",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_Nome_Sku",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "TextoBusca",
                table: "Produtos");
        }
    }
}
