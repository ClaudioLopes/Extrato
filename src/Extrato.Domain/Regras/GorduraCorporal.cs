namespace Extrato.Domain.Regras;

/// <summary>Seção 5.3: % de gordura pelo método da Marinha americana (só tendência).</summary>
public static class GorduraCorporal
{
    public static decimal? MarinhaAmericana(Sexo sexo, decimal alturaCm, decimal cinturaCm, decimal pescocoCm, decimal? quadrilCm = null)
    {
        double altura = (double)alturaCm, cintura = (double)cinturaCm, pescoco = (double)pescocoCm;
        double resultado;
        if (sexo == Sexo.Masculino)
        {
            var dif = cintura - pescoco;
            if (dif <= 0) return null;
            resultado = 495 / (1.0324 - 0.19077 * Math.Log10(dif) + 0.15456 * Math.Log10(altura)) - 450;
        }
        else
        {
            if (quadrilCm is null) return null;
            var soma = cintura + (double)quadrilCm.Value - pescoco;
            if (soma <= 0) return null;
            resultado = 495 / (1.29579 - 0.35004 * Math.Log10(soma) + 0.22100 * Math.Log10(altura)) - 450;
        }

        return resultado is < 2 or > 70 ? null : Math.Round((decimal)resultado, 1);
    }
}
