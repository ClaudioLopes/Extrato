# ADR 0002 — Macros vêm da tabela (TACO/rótulo), não da IA

**Status:** aceito

## Contexto
Modelos de visão estimam mal calorias, e o erro se acumula ao longo do dia.

## Decisão
- A IA só **identifica** o alimento e estima **gramas** (com confiança 0–1). O prompt proíbe calcular calorias.
- Calorias e macros são sempre calculados no backend: `gramas / 100 × valor por 100 g`,
  a partir da TACO ou de alimentos cadastrados por rótulo (industrializados como whey).
- O match nome-da-IA → alimento: normalização (minúsculas, sem acento) → `AlimentoSinonimo` →
  similaridade por trigramas. Abaixo do limiar o item fica "não identificado" e o usuário escolhe.
- Itens que batem com alimentos evitados aparecem sinalizados (provável erro de identificação).
- `GramasSugeridasIA` × `Gramas` confirmadas permite medir o erro da IA ao longo do tempo
  (exibido na tela de Progresso).
