using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Definicion
{
    public class ColumnaDefinicionIndiceModelo
    {
        public string Nombre { get; set; } = "";

        public bool Descendente { get; set; }
    }

    public class DefinicionIndiceModelo
    {
        public TablaModelo Tabla { get; set; } = new();

        public string Nombre { get; set; } = "";

        public List<ColumnaDefinicionIndiceModelo> Columnas { get; set; } = [];

        public bool EsUnico { get; set; }

        public bool EsAgrupado { get; set; }

        public List<string> Incluidas { get; set; } = [];

        public string? Filtro { get; set; }

        public string? Metodo { get; set; }

        public bool Concurrente { get; set; }
    }

    public class DefinicionCheckModelo
    {
        public TablaModelo Tabla { get; set; } = new();

        public string Nombre { get; set; } = "";

        public string Condicion { get; set; } = "";

        public bool ComprobarExistentes { get; set; } = true;
    }

    public enum ComparacionCheck
    {
        MayorOIgual,
        MayorQue,
        MenorOIgual,
        MenorQue,
        Distinto,
        Entre,
        EnLista,
        NoVacio
    }

    public static class AsistenteCheckService
    {
        public static string Texto(this ComparacionCheck comparacion) => comparacion switch
        {
            ComparacionCheck.MayorOIgual => "mayor o igual que (>=)",
            ComparacionCheck.MayorQue => "mayor que (>)",
            ComparacionCheck.MenorOIgual => "menor o igual que (<=)",
            ComparacionCheck.MenorQue => "menor que (<)",
            ComparacionCheck.Distinto => "distinto de (<>)",
            ComparacionCheck.Entre => "entre dos valores (BETWEEN)",
            ComparacionCheck.EnLista => "uno de una lista (IN)",
            _ => "no vacío"
        };

        public static bool UsaValor(this ComparacionCheck comparacion) => comparacion != ComparacionCheck.NoVacio;

        public static string? Condicion(
            DialectoSqlModelo dialecto,
            string columna,
            string tipoDatos,
            ComparacionCheck comparacion,
            string? valor,
            string? segundoValor)
        {
            var c = dialecto.Columna(columna);
            string Literal(string v) => dialecto.Literal(tipoDatos, v.Trim());

            if (comparacion == ComparacionCheck.NoVacio)
            {
                return dialecto.NoVacio(c);
            }

            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }

            return comparacion switch
            {
                ComparacionCheck.MayorOIgual => $"{c} >= {Literal(valor)}",
                ComparacionCheck.MayorQue => $"{c} > {Literal(valor)}",
                ComparacionCheck.MenorOIgual => $"{c} <= {Literal(valor)}",
                ComparacionCheck.MenorQue => $"{c} < {Literal(valor)}",
                ComparacionCheck.Distinto => $"{c} <> {Literal(valor)}",
                ComparacionCheck.Entre => string.IsNullOrWhiteSpace(segundoValor) ? null : $"{c} BETWEEN {Literal(valor)} AND {Literal(segundoValor)}",
                _ => $"{c} IN ({string.Join(", ", valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Literal))})"
            };
        }
    }
}
