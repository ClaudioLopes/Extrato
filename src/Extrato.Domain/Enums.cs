namespace Extrato.Domain;

public enum Sexo { Masculino = 1, Feminino = 2 }

public enum NivelAtividade { Sedentario = 1, Leve = 2, Moderado = 3, Alto = 4, MuitoAlto = 5 }

/// <summary>Um modo ativo por vez: misturar ganho e definição gera metas contraditórias.</summary>
public enum Objetivo { GanhoMassa = 1, Recomposicao = 2, Definicao = 3 }

public enum TipoRefeicao { CafeDaManha = 1, Almoco = 2, Tarde = 3, Noite = 4 }

public enum OrigemRefeicao { Foto = 1, Manual = 2, Favorito = 3 }

public enum OrigemMeta { Inicial = 1, Manual = 2, Ajuste = 3 }

public enum FonteAlimento { Taco = 1, Personalizado = 2 }

public enum TipoCarga { Pilha = 1, Anilha = 2, Halter = 3, PesoCorporal = 4 }
