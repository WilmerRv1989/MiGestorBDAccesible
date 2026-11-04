using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Ejecucion
{
    public record SentenciaPostgreSql(string Texto, int LineaInicial);

    public static partial class DivisorPostgreSql
    {
        [GeneratedRegex(@"^\$([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_\u0080-\uFFFF]*)?\$")]
        private static partial Regex EtiquetaDolar();

        public static List<SentenciaPostgreSql> Dividir(string script)
        {
            var texto = script.Replace("\r\n", "\n");
            var sentencias = new List<SentenciaPostgreSql>();
            var actual = new StringBuilder();
            var linea = 1;
            var lineaInicial = 0;
            var empezada = false;
            var i = 0;

            void Cerrar()
            {
                var contenido = actual.ToString().TrimEnd();

                if (empezada && contenido.Length > 0)
                {
                    sentencias.Add(new SentenciaPostgreSql(contenido, lineaInicial));
                }

                actual.Clear();
                empezada = false;
            }

            void Agregar(string fragmento, bool significativo)
            {
                if (!empezada)
                {
                    if (!significativo)
                    {
                        return;
                    }

                    empezada = true;
                    lineaInicial = linea;
                }

                actual.Append(fragmento);
                linea += fragmento.Count(c => c == '\n');
            }

            while (i < texto.Length)
            {
                var c = texto[i];

                if (c == '-' && Siguiente(texto, i) == '-')
                {
                    var fin = texto.IndexOf('\n', i);
                    fin = fin < 0 ? texto.Length : fin;
                    Agregar(texto[i..fin], false);
                    i = fin;
                    continue;
                }

                if (c == '/' && Siguiente(texto, i) == '*')
                {
                    var fin = FinComentario(texto, i);
                    var comentario = texto[i..fin];

                    if (empezada)
                    {
                        Agregar(comentario, false);
                    }
                    else
                    {
                        linea += comentario.Count(ch => ch == '\n');
                    }

                    i = fin;
                    continue;
                }

                if (c == '$' && !EsParteDeNombre(texto, i) && EtiquetaDolar().Match(texto[i..]) is { Success: true } etiqueta)
                {
                    var cierre = texto.IndexOf(etiqueta.Value, i + etiqueta.Length, StringComparison.Ordinal);

                    if (cierre < 0)
                    {
                        throw new ScriptNoDivisibleException(
                            "Hay un texto entre signos de dólar sin cerrar. Ciérralo con la misma etiqueta y vuelve a ejecutar. No se ejecutó nada.",
                            linea);
                    }

                    var fin = cierre + etiqueta.Length;
                    Agregar(texto[i..fin], true);
                    i = fin;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    var conEscapes = c == '\'' && i > 0 && texto[i - 1] is 'E' or 'e' && !EsParteDeNombre(texto, i - 1);
                    var fin = FinCadena(texto, i, conEscapes);
                    Agregar(texto[i..fin], true);
                    i = fin;
                    continue;
                }

                if (c == '\\' && !empezada)
                {
                    var fin = texto.IndexOf('\n', i);
                    fin = fin < 0 ? texto.Length : fin;
                    Agregar(texto[i..fin], true);
                    Cerrar();
                    i = fin;
                    continue;
                }

                if (c == ';')
                {
                    Cerrar();
                    i++;
                    continue;
                }

                if (c == '\n')
                {
                    if (empezada)
                    {
                        Agregar("\n", false);
                    }
                    else
                    {
                        linea++;
                    }

                    i++;
                    continue;
                }

                Agregar(c.ToString(), !char.IsWhiteSpace(c));
                i++;
            }

            Cerrar();
            return sentencias;
        }

        public static bool EsComandoPsql(string sentencia) => sentencia.TrimStart().StartsWith('\\');

        private static char Siguiente(string texto, int i) => i + 1 < texto.Length ? texto[i + 1] : '\0';

        private static bool EsParteDeNombre(string texto, int i) =>
            i > 0 && (texto[i - 1] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '$' or >= '\u0080');

        private static int FinComentario(string texto, int i)
        {
            var profundidad = 0;
            var j = i;

            while (j < texto.Length)
            {
                if (texto[j] == '/' && Siguiente(texto, j) == '*')
                {
                    profundidad++;
                    j += 2;
                }
                else if (texto[j] == '*' && Siguiente(texto, j) == '/')
                {
                    profundidad--;
                    j += 2;

                    if (profundidad == 0)
                    {
                        return j;
                    }
                }
                else
                {
                    j++;
                }
            }

            return texto.Length;
        }

        private static int FinCadena(string texto, int i, bool conEscapes)
        {
            var comilla = texto[i];
            var j = i + 1;

            while (j < texto.Length)
            {
                if (conEscapes && texto[j] == '\\')
                {
                    j += 2;
                    continue;
                }

                if (texto[j] == comilla)
                {
                    if (Siguiente(texto, j) == comilla)
                    {
                        j += 2;
                        continue;
                    }

                    return j + 1;
                }

                j++;
            }

            return texto.Length;
        }
    }
}
