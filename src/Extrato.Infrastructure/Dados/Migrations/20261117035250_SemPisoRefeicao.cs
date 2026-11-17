using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    /// <summary>O piso das refeições deixa de existir por padrão: quem estava no padrão antigo (0,5) passa a 0.</summary>
    public partial class SemPisoRefeicao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"ConfiguracaoRegras\" SET \"PisoRefeicao\" = 0 WHERE \"PisoRefeicao\" = 0.5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"ConfiguracaoRegras\" SET \"PisoRefeicao\" = 0.5 WHERE \"PisoRefeicao\" = 0;");
        }
    }
}
