using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Ejecucion
{
    public record SentenciaOracle(string Texto, int LineaInicial, bool EsPlSql);

    public static partial class DivisorOracle
    {
        [GeneratedRegex(@"^\s*(BEGIN|DECLARE|CREATE\s+(OR\s+REPLACE\s+)?((NON)?EDITIONABLE\s+)?(PROCEDURE|FUNCTION|PACKAGE|TRIGGER|TYPE|LIBRARY)\b)", RegexOptions.IgnoreCase)]
        private static partial Regex InicioPlSql();

        public static List<SentenciaOracle> Dividir(string script)
        {
            var texto = script.Replace("\r\n", "\n");
            var sentencias = new List<SentenciaOracle>();
            var actual = new StringBuilder();
            var linea = 1;
            var lineaInicial = 0;
            bool? esPlSql = null;
            var i = 0;

            void Cerrar()
            {
                var contenido = actual.ToString().TrimEnd();

                if (esPlSql is not null && contenido.Length > 0)
                {
                    sentencias.Add(new SentenciaOracle(contenido, lineaInicial, esPlSql.Value));
                }

                actual.Clear();
                esPlSql = null;
            }

            void Agregar(string fragmento, bool significativo)
            {
                if (esPlSql is null)
                {
                    if (!significativo)
                    {
                        return;
                    }

                    lineaInicial = linea;
                    esPlSql = InicioPlSql().IsMatch(texto[i..]);
                }

                actual.Append(fragmento);
            }

            while (i < texto.Length)
            {
                var c = texto[i];

                if (EsInicioDeLinea(texto, i) && EsLineaBarra(texto, i, out var finBarra))
                {
                    Cerrar();
                    linea += texto[i..finBarra].Count(ch => ch == '\n');
                    i = finBarra;
                    continue;
                }

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
                    var fin = texto.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    fin = fin < 0 ? texto.Length : fin + 2;
                    var comentario = texto[i..fin];
                    Agregar(comentario, false);
                    linea += comentario.Count(ch => ch == '\n');
                    i = fin;
                    continue;
                }

                if (c == '\'' || c == '"' || ((c is 'q' or 'Q' or 'n' or 'N') && EsComillaQ(texto, i)))
                {
                    var fin = FinDeCadena(texto, i);
                    var cadena = texto[i..fin];
                    Agregar(cadena, true);
                    linea += cadena.Count(ch => ch == '\n');
                    i = fin;
                    continue;
                }

                if (c == ';' && esPlSql == false)
                {
                    Cerrar();
                    i++;
                    continue;
                }

                if (c == '\n')
                {
                    if (esPlSql is not null)
                    {
                        actual.Append(c);
                    }

                    linea++;
                    i++;
                    continue;
                }

                Agregar(c.ToString(), !char.IsWhiteSpace(c));
                i++;
            }

            Cerrar();
            return sentencias;
        }

        private static char Siguiente(string texto, int i) => i + 1 < texto.Length ? texto[i + 1] : '\0';

        private static bool EsInicioDeLinea(string texto, int i)
        {
            for (var j = i - 1; j >= 0 && texto[j] != '\n'; j--)
            {
                if (!char.IsWhiteSpace(texto[j]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EsLineaBarra(string texto, int i, out int fin)
        {
            var j = i;

            while (j < texto.Length && texto[j] is ' ' or '\t')
            {
                j++;
            }

            if (j >= texto.Length || texto[j] != '/')
            {
                fin = i;
                return false;
            }

            j++;

            while (j < texto.Length && texto[j] is ' ' or '\t')
            {
                j++;
            }

            fin = j;
            return j >= texto.Length || texto[j] == '\n';
        }

        private static bool EsComillaQ(string texto, int i)
        {
            if (i > 0 && (char.IsLetterOrDigit(texto[i - 1]) || texto[i - 1] is '_' or '$' or '#'))
            {
                return false;
            }

            var j = i;

            if (texto[j] is 'n' or 'N')
            {
                j++;

                if (j < texto.Length && texto[j] == '\'')
                {
                    return true;
                }
            }

            return j + 2 < texto.Length && texto[j] is 'q' or 'Q' && texto[j + 1] == '\'';
        }

        private static int FinDeCadena(string texto, int i)
        {
            var j = i;

            if (texto[j] is 'n' or 'N')
            {
                j++;
            }

            if (texto[j] is 'q' or 'Q')
            {
                var apertura = texto[j + 2];
                var cierre = apertura switch { '[' => ']', '{' => '}', '(' => ')', '<' => '>', _ => apertura };
                var fin = texto.IndexOf($"{cierre}'", j + 3, StringComparison.Ordinal);
                return fin < 0 ? texto.Length : fin + 2;
            }

            var comilla = texto[j];
            j++;

            while (j < texto.Length)
            {
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
