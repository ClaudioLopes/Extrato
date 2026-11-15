using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    public partial class ExecucaoJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecucaoJob",
                columns: table => new
                {
                    Nome = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    UltimaExecucao = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecucaoJob", x => x.Nome);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecucaoJob");
        }
    }
}
