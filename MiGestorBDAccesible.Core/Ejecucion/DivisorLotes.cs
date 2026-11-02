using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Ejecucion
{
    public record LoteModelo(string Texto, int LineaInicial, int Repeticiones);

    public static partial class DivisorLotes
    {
        [GeneratedRegex(@"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase)]
        private static partial Regex SeparadorGo();

        public static List<LoteModelo> Dividir(string script)
        {
            var lotes = new List<LoteModelo>();
            var actual = new StringBuilder();
            var lineaInicial = 1;
            var lineas = script.Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lineas.Length; i++)
            {
                var coincidencia = SeparadorGo().Match(lineas[i]);

                if (coincidencia.Success)
                {
                    var repeticiones = coincidencia.Groups[1].Success ? int.Parse(coincidencia.Groups[1].Value) : 1;
                    Agregar(lotes, actual, lineaInicial, repeticiones);
                    actual.Clear();
                    lineaInicial = i + 2;
                }
                else
                {
                    actual.Append(lineas[i]).Append('\n');
                }
            }

            Agregar(lotes, actual, lineaInicial, 1);
            return lotes;
        }

        private static void Agregar(List<LoteModelo> lotes, StringBuilder texto, int lineaInicial, int repeticiones)
        {
            var contenido = texto.ToString();

            if (!string.IsNullOrWhiteSpace(contenido))
            {
                lotes.Add(new LoteModelo(contenido, lineaInicial, Math.Max(1, repeticiones)));
            }
        }
    }
}
