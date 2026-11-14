using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    public partial class TreinoOffline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdCliente",
                table: "SessaoTreino",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdCliente",
                table: "Serie",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessaoTreino_IdCliente",
                table: "SessaoTreino",
                column: "IdCliente",
                unique: true,
                filter: "[IdCliente] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Serie_IdCliente",
                table: "Serie",
                column: "IdCliente",
                unique: true,
                filter: "[IdCliente] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessaoTreino_IdCliente",
                table: "SessaoTreino");

            migrationBuilder.DropIndex(
                name: "IX_Serie_IdCliente",
                table: "Serie");

            migrationBuilder.DropColumn(
                name: "IdCliente",
                table: "SessaoTreino");

            migrationBuilder.DropColumn(
                name: "IdCliente",
                table: "Serie");
        }
    }
}
