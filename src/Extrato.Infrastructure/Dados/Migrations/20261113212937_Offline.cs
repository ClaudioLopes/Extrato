using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Offline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdCliente",
                table: "Refeicao",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refeicao_UsuarioId_IdCliente",
                table: "Refeicao",
                columns: new[] { "UsuarioId", "IdCliente" },
                unique: true,
                filter: "[IdCliente] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refeicao_UsuarioId_IdCliente",
                table: "Refeicao");

            migrationBuilder.DropColumn(
                name: "IdCliente",
                table: "Refeicao");
        }
    }
}
