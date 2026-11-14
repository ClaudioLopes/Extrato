using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Atividades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AtividadesNaMeta",
                table: "ConfiguracaoRegras",
                type: "bit",
                nullable: false,
                defaultValue: true); // regras já gravadas passam a somar atividades, como o padrão do app

            migrationBuilder.AddColumn<int>(
                name: "PercentualKcalAtividade",
                table: "ConfiguracaoRegras",
                type: "int",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.CreateTable(
                name: "Atividade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    DataHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DuracaoMin = table.Column<int>(type: "int", nullable: false),
                    KcalAtivas = table.Column<int>(type: "int", nullable: false),
                    KcalTotais = table.Column<int>(type: "int", nullable: true),
                    DistanciaM = table.Column<int>(type: "int", nullable: true),
                    FcMedia = table.Column<int>(type: "int", nullable: true),
                    Origem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Atividade", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Atividade_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Atividade_UsuarioId_DataHora",
                table: "Atividade",
                columns: new[] { "UsuarioId", "DataHora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Atividade");

            migrationBuilder.DropColumn(
                name: "AtividadesNaMeta",
                table: "ConfiguracaoRegras");

            migrationBuilder.DropColumn(
                name: "PercentualKcalAtividade",
                table: "ConfiguracaoRegras");
        }
    }
}
