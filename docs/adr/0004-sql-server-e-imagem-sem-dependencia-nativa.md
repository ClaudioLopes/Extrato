# ADR 0004 — SQL Server no lugar do PostgreSQL; imagem sem dependência nativa

**Status:** aceito (desvio da documentação original)

## Banco: SQL Server + EF Core
A especificação previa PostgreSQL com `pg_trgm` para busca aproximada. O projeto usa
**SQL Server** (Express/LocalDB em dev, imagem `mssql/server` no Docker).

- A busca aproximada por trigramas foi reimplementada em C# (`TextoBusca.Similaridade` /
  `SimilaridadeConsulta`, equivalentes a `similarity()`/`word_similarity()` do `pg_trgm`).
  A base de alimentos é pequena (TACO ≈ 600 itens), então o ranking em memória é barato e
  fica testável sem banco. Se a base crescer, dá para mover para Full-Text Search do SQL Server.
- `PesosRefeicao` e `Travas` ficam em colunas JSON (`nvarchar`) via value converter, como no modelo da doc.

## Imagem: redimensionamento no cliente + sanitização no servidor
A doc previa ImageSharp. Na data da implementação:
- ImageSharp ≤ 3.1.12 tem vulnerabilidades altas conhecidas no decodificador (GHSA-…),
  inaceitável para um endpoint que recebe uploads;
- ImageSharp ≥ 3.2 exige chave de licença em builds Release (quebra CI e Docker).
- SkiaSharp traz binários nativos de várias plataformas (centenas de MB).

Decisão:
1. **No cliente (Blazor):** `IBrowserFile.RequestImageFileAsync("image/jpeg", 1024, 1024)` redimensiona
   pelo canvas do navegador e recodifica, descartando EXIF/GPS **antes** do upload (e economiza dados móveis).
2. **No servidor:** `SanitizadorImagem` (C# puro) valida a assinatura (JPEG/PNG), confere dimensões
   (≤ 8192 px) e reescreve o arquivo sem metadados (remove APP1–APP15 e COM do JPEG; mantém só
   IHDR/PLTE/IDAT/IEND/tRNS do PNG). Tudo em memória.

O servidor nunca decodifica pixels, o que elimina a superfície de ataque do decodificador.
Clientes que chamam a API diretamente com imagens grandes ainda são aceitos (até 15 MB/8192 px);
o provedor de IA redimensiona do lado dele.
