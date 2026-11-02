using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Definicion
{
    public class DefinicionColumnaModelo
    {
        public string Nombre { get; set; } = "";

        public string Tipo { get; set; } = "";

        public int? Longitud { get; set; }

        public bool EsMax { get; set; }

        public int? Precision { get; set; }

        public int? Escala { get; set; }

        public int? FraccionSegundos { get; set; }

        public bool PermiteNulos { get; set; }

        public bool EsClavePrincipal { get; set; }

        public bool EsUnica { get; set; }

        public bool EsIdentidad { get; set; }

        public long Semilla { get; set; } = 1;

        public long Incremento { get; set; } = 1;

        public string? ValorPredeterminado { get; set; }

        public ReferenciaModelo? Referencia { get; set; }

        public string? NombreOriginal { get; set; }

        public string? Formula { get; set; }

        public bool EsPersistente { get; set; }

        public bool EsCalculada => Formula is not null;

        public string TipoCompleto
        {
            get
            {
                if (EsMax)
                {
                    return $"{Tipo}(max)";
                }

                if (Longitud is int longitud)
                {
                    return $"{Tipo}({longitud})";
                }

                if (Precision is int precision)
                {
                    return $"{Tipo}({precision}, {Escala ?? 0})";
                }

                if (FraccionSegundos is not int fraccion)
                {
                    return Tipo;
                }

                var espacio = Tipo.IndexOf(' ');
                return espacio < 0 ? $"{Tipo}({fraccion})" : $"{Tipo[..espacio]}({fraccion}){Tipo[espacio..]}";
            }
        }

        public bool AplicarTipo(string tipoCompleto, IReadOnlyList<TipoDatosModelo> tipos)
        {
            var parentesis = Regex.Match(tipoCompleto, @"\(([^)]*)\)");
            var nombre = Regex.Replace(tipoCompleto, @"\s*\([^)]*\)", "").Trim();
            var tipo = tipos.FirstOrDefault(t => t.Nombre == nombre);

            if (tipo is null)
            {
                return false;
            }

            var argumentos = parentesis.Success
                ? parentesis.Groups[1].Value.Split(',').Select(a => a.Trim()).ToArray()
                : [];

            int? Numero(int indice) =>
                indice < argumentos.Length && Regex.Match(argumentos[indice], @"^\d+") is { Success: true } digitos
                    ? int.Parse(digitos.Value)
                    : null;

            Tipo = tipo.Nombre;
            EsMax = argumentos.FirstOrDefault() == "max";
            Longitud = tipo.UsaLongitud && !EsMax ? Numero(0) : null;
            Precision = tipo.UsaPrecision ? Numero(0) : null;
            Escala = tipo.UsaPrecision ? Numero(1) : null;
            FraccionSegundos = tipo.UsaFraccionSegundos ? Numero(0) ?? tipo.FraccionPredeterminada : null;

            if (!tipo.AdmiteIdentidad)
            {
                EsIdentidad = false;
            }

            return true;
        }

        public DefinicionColumnaModelo Copiar()
        {
            var copia = (DefinicionColumnaModelo)MemberwiseClone();
            copia.Referencia = Referencia?.Copiar();
            return copia;
        }
    }

    public class DefinicionTablaModelo
    {
        public string Esquema { get; set; } = "dbo";

        public string Nombre { get; set; } = "";

        public List<DefinicionColumnaModelo> Columnas { get; } = [];
    }
}
