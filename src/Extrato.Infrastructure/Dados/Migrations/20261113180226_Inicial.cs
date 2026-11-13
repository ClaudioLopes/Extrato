using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extrato.Infrastructure.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Academia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Academia", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Alimento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NomeNormalizado = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Fonte = table.Column<int>(type: "int", nullable: false),
                    Kcal100g = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    Proteina100g = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Carbo100g = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Gordura100g = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Fibra100g = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alimento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rotina",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rotina", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Sexo = table.Column<int>(type: "int", nullable: false),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: false),
                    AlturaCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    NivelAtividade = table.Column<int>(type: "int", nullable: false),
                    Objetivo = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuario", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Aparelho",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcademiaId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Exercicio = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrupoMuscular = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TipoCarga = table.Column<int>(type: "int", nullable: false),
                    IncrementoKg = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aparelho", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Aparelho_Academia_AcademiaId",
                        column: x => x.AcademiaId,
                        principalTable: "Academia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlimentoSinonimo",
                columns: table => new
                {
                    AlimentoId = table.Column<int>(type: "int", nullable: false),
                    Termo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlimentoSinonimo", x => new { x.AlimentoId, x.Termo });
                    table.ForeignKey(
                        name: "FK_AlimentoSinonimo_Alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "Alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessaoTreino",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RotinaId = table.Column<int>(type: "int", nullable: true),
                    Observacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessaoTreino", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessaoTreino_Rotina_RotinaId",
                        column: x => x.RotinaId,
                        principalTable: "Rotina",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AjusteMeta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    KcalAnterior = table.Column<int>(type: "int", nullable: false),
                    KcalNova = table.Column<int>(type: "int", nullable: false),
                    GetEstimado = table.Column<int>(type: "int", nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjusteMeta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AjusteMeta_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlimentoEvitado",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    AlimentoId = table.Column<int>(type: "int", nullable: true),
                    Termo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlimentoEvitado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlimentoEvitado_Alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "Alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AlimentoEvitado_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracaoRegras",
                columns: table => new
                {
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    ProteinaGPorKg = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    GorduraGPorKg = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    AjusteKcal = table.Column<int>(type: "int", nullable: true),
                    FatorAtividade = table.Column<decimal>(type: "decimal(5,3)", precision: 5, scale: 3, nullable: true),
                    PesosRefeicao = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Travas = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    HoraAlertaProteina = table.Column<int>(type: "int", nullable: false),
                    PisoRefeicao = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracaoRegras", x => x.UsuarioId);
                    table.ForeignKey(
                        name: "FK_ConfiguracaoRegras_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Medidas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    CinturaCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PescocoCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    QuadrilCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    ObservacaoCorporal = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medidas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Medidas_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetaDiaria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    Kcal = table.Column<int>(type: "int", nullable: false),
                    ProteinaG = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    CarboG = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    GorduraG = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Origem = table.Column<int>(type: "int", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaDiaria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaDiaria_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PorcaoFavorita",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TipoSugerido = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PorcaoFavorita", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PorcaoFavorita_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Refeicao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    DataHora = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Origem = table.Column<int>(type: "int", nullable: false),
                    DescricaoUsuario = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refeicao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Refeicao_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistroPeso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    PesoKg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistroPeso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistroPeso_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RotinaItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RotinaId = table.Column<int>(type: "int", nullable: false),
                    AparelhoId = table.Column<int>(type: "int", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    SeriesAlvo = table.Column<int>(type: "int", nullable: false),
                    RepsMin = table.Column<int>(type: "int", nullable: false),
                    RepsMax = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotinaItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotinaItem_Aparelho_AparelhoId",
                        column: x => x.AparelhoId,
                        principalTable: "Aparelho",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RotinaItem_Rotina_RotinaId",
                        column: x => x.RotinaId,
                        principalTable: "Rotina",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Serie",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessaoId = table.Column<int>(type: "int", nullable: false),
                    AparelhoId = table.Column<int>(type: "int", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    CargaKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    Reps = table.Column<int>(type: "int", nullable: false),
                    RepsNaReserva = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Serie", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Serie_Aparelho_AparelhoId",
                        column: x => x.AparelhoId,
                        principalTable: "Aparelho",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Serie_SessaoTreino_SessaoId",
                        column: x => x.SessaoId,
                        principalTable: "SessaoTreino",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PorcaoFavoritaItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PorcaoFavoritaId = table.Column<int>(type: "int", nullable: false),
                    AlimentoId = table.Column<int>(type: "int", nullable: false),
                    Gramas = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PorcaoFavoritaItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PorcaoFavoritaItem_Alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "Alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PorcaoFavoritaItem_PorcaoFavorita_PorcaoFavoritaId",
                        column: x => x.PorcaoFavoritaId,
                        principalTable: "PorcaoFavorita",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemRefeicao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RefeicaoId = table.Column<int>(type: "int", nullable: false),
                    AlimentoId = table.Column<int>(type: "int", nullable: false),
                    Gramas = table.Column<decimal>(type: "decimal(7,1)", precision: 7, scale: 1, nullable: false),
                    Kcal = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ProteinaG = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    CarboG = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    GorduraG = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    GramasSugeridasIA = table.Column<decimal>(type: "decimal(7,1)", precision: 7, scale: 1, nullable: true),
                    ConfiancaIA = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    EditadoPeloUsuario = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemRefeicao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemRefeicao_Alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "Alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemRefeicao_Refeicao_RefeicaoId",
                        column: x => x.RefeicaoId,
                        principalTable: "Refeicao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AjusteMeta_UsuarioId_Data",
                table: "AjusteMeta",
                columns: new[] { "UsuarioId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_Alimento_NomeNormalizado",
                table: "Alimento",
                column: "NomeNormalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlimentoEvitado_AlimentoId",
                table: "AlimentoEvitado",
                column: "AlimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_AlimentoEvitado_UsuarioId",
                table: "AlimentoEvitado",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_AlimentoSinonimo_Termo",
                table: "AlimentoSinonimo",
                column: "Termo");

            migrationBuilder.CreateIndex(
                name: "IX_Aparelho_AcademiaId",
                table: "Aparelho",
                column: "AcademiaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRefeicao_AlimentoId",
                table: "ItemRefeicao",
                column: "AlimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRefeicao_RefeicaoId",
                table: "ItemRefeicao",
                column: "RefeicaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Medidas_UsuarioId_Data",
                table: "Medidas",
                columns: new[] { "UsuarioId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_MetaDiaria_UsuarioId_VigenteDesde",
                table: "MetaDiaria",
                columns: new[] { "UsuarioId", "VigenteDesde" });

            migrationBuilder.CreateIndex(
                name: "IX_PorcaoFavorita_UsuarioId",
                table: "PorcaoFavorita",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_PorcaoFavoritaItem_AlimentoId",
                table: "PorcaoFavoritaItem",
                column: "AlimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PorcaoFavoritaItem_PorcaoFavoritaId",
                table: "PorcaoFavoritaItem",
                column: "PorcaoFavoritaId");

            migrationBuilder.CreateIndex(
                name: "IX_Refeicao_UsuarioId_DataHora",
                table: "Refeicao",
                columns: new[] { "UsuarioId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistroPeso_UsuarioId_Data",
                table: "RegistroPeso",
                columns: new[] { "UsuarioId", "Data" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RotinaItem_AparelhoId",
                table: "RotinaItem",
                column: "AparelhoId");

            migrationBuilder.CreateIndex(
                name: "IX_RotinaItem_RotinaId",
                table: "RotinaItem",
                column: "RotinaId");

            migrationBuilder.CreateIndex(
                name: "IX_Serie_AparelhoId_SessaoId",
                table: "Serie",
                columns: new[] { "AparelhoId", "SessaoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Serie_SessaoId",
                table: "Serie",
                column: "SessaoId");

            migrationBuilder.CreateIndex(
                name: "IX_SessaoTreino_DataHora",
                table: "SessaoTreino",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_SessaoTreino_RotinaId",
                table: "SessaoTreino",
                column: "RotinaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AjusteMeta");

            migrationBuilder.DropTable(
                name: "AlimentoEvitado");

            migrationBuilder.DropTable(
                name: "AlimentoSinonimo");

            migrationBuilder.DropTable(
                name: "ConfiguracaoRegras");

            migrationBuilder.DropTable(
                name: "ItemRefeicao");

            migrationBuilder.DropTable(
                name: "Medidas");

            migrationBuilder.DropTable(
                name: "MetaDiaria");

            migrationBuilder.DropTable(
                name: "PorcaoFavoritaItem");

            migrationBuilder.DropTable(
                name: "RegistroPeso");

            migrationBuilder.DropTable(
                name: "RotinaItem");

            migrationBuilder.DropTable(
                name: "Serie");

            migrationBuilder.DropTable(
                name: "Refeicao");

            migrationBuilder.DropTable(
                name: "Alimento");

            migrationBuilder.DropTable(
                name: "PorcaoFavorita");

            migrationBuilder.DropTable(
                name: "Aparelho");

            migrationBuilder.DropTable(
                name: "SessaoTreino");

            migrationBuilder.DropTable(
                name: "Usuario");

            migrationBuilder.DropTable(
                name: "Academia");

            migrationBuilder.DropTable(
                name: "Rotina");
        }
    }
}
