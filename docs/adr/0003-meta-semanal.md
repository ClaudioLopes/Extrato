# ADR 0003 — A meta muda por semana, não por refeição

**Status:** aceito

## Contexto
Peso diário oscila com água e sódio; reagir a cada refeição ou pesagem gera metas instáveis.

## Decisão
- **Dentro do dia** a meta é fixa. O restante (`meta − consumido`) é redistribuído entre as
  refeições que faltam, proporcional aos pesos (café 25%, almoço 35%, tarde 15%, noite 25%),
  sem piso por padrão: passou da meta, o alvo das próximas cai até 0 (antes havia piso de 50%, que travava
  os alvos e escondia o excesso). O piso continua opcional em Ajustes.
- **Entre semanas**, a meta só muda pela tendência: média móvel de 7 dias e GET adaptativo
  (`ingestão média − Δpeso × 7.700 / 7`), exigindo ≥ 5 dias registrados.
- Só ajusta se o ritmo ficar fora da faixa do objetivo por 2 semanas seguidas, ±125 kcal,
  no máximo 1 vez por semana, com travas (nunca abaixo da TMB, máx. 150 kcal/semana).
- Todo ajuste grava um `AjusteMeta` com o motivo legível, incluindo a leitura de força × cintura do módulo de treino.
