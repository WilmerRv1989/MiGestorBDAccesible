using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Ejecucion
{
    public record LoteModelo(string Texto, int LineaInicial, int Repeticiones);

    public static partial class DivisorLotes
    {
        public const int RepeticionesMaximas = 10000;

        [GeneratedRegex(@"^\s*GO(?:\s+([0-9]+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase)]
        private static partial Regex SeparadorGo();

        public static List<LoteModelo> Dividir(string script)
        {
            var lotes = new List<LoteModelo>();
            var actual = new StringBuilder();
            var lineaInicial = 1;
            var lineas = script.Replace("\r\n", "\n").Split('\n');
            var comilla = '\0';
            var profundidadComentario = 0;

            for (var i = 0; i < lineas.Length; i++)
            {
                var coincidencia = comilla == '\0' && profundidadComentario == 0
                    ? SeparadorGo().Match(lineas[i])
                    : null;

                if (coincidencia is { Success: true })
                {
                    var repeticiones = 1;

                    if (coincidencia.Groups[1].Success &&
                        (!int.TryParse(coincidencia.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out repeticiones) ||
                         repeticiones > RepeticionesMaximas))
                    {
                        throw new ScriptNoDivisibleException(
                            $"GO supera el máximo de {RepeticionesMaximas.ToString(CultureInfo.InvariantCulture)} repeticiones por lote. Reduce las repeticiones y vuelve a ejecutar. No se ejecutó nada.",
                            i + 1);
                    }

                    Agregar(lotes, actual, lineaInicial, repeticiones);
                    actual.Clear();
                    lineaInicial = i + 2;
                }
                else
                {
                    actual.Append(lineas[i]).Append('\n');
                    RecorrerLinea(lineas[i], ref comilla, ref profundidadComentario);
                }
            }

            Agregar(lotes, actual, lineaInicial, 1);
            return lotes;
        }

        private static void RecorrerLinea(string linea, ref char comilla, ref int profundidadComentario)
        {
            for (var i = 0; i < linea.Length; i++)
            {
                var c = linea[i];
                var siguiente = i + 1 < linea.Length ? linea[i + 1] : '\0';

                if (profundidadComentario > 0)
                {
                    if (c == '/' && siguiente == '*')
                    {
                        profundidadComentario++;
                        i++;
                    }
                    else if (c == '*' && siguiente == '/')
                    {
                        profundidadComentario--;
                        i++;
                    }
                }
                else if (comilla != '\0')
                {
                    if (c == comilla)
                    {
                        if (siguiente == comilla)
                        {
                            i++;
                        }
                        else
                        {
                            comilla = '\0';
                        }
                    }
                }
                else if (c == '-' && siguiente == '-')
                {
                    break;
                }
                else if (c == '/' && siguiente == '*')
                {
                    profundidadComentario++;
                    i++;
                }
                else if (c is '\'' or '"' or '[')
                {
                    comilla = c == '[' ? ']' : c;
                }
            }
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
