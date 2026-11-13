using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Fase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GorduraFotoMaxPercent",
                table: "Medidas",
                type: "decimal(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GorduraFotoMinPercent",
                table: "Medidas",
                type: "decimal(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CicloTreinoAtivo",
                table: "ConfiguracaoRegras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DiasTreino",
                table: "ConfiguracaoRegras",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "1,2,3,4,5");

            migrationBuilder.AddColumn<int>(
                name: "KcalExtraDiaTreino",
                table: "ConfiguracaoRegras",
                type: "int",
                nullable: false,
                defaultValue: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GorduraFotoMaxPercent",
                table: "Medidas");

            migrationBuilder.DropColumn(
                name: "GorduraFotoMinPercent",
                table: "Medidas");

            migrationBuilder.DropColumn(
                name: "CicloTreinoAtivo",
                table: "ConfiguracaoRegras");

            migrationBuilder.DropColumn(
                name: "DiasTreino",
                table: "ConfiguracaoRegras");

            migrationBuilder.DropColumn(
                name: "KcalExtraDiaTreino",
                table: "ConfiguracaoRegras");
        }
    }
}
