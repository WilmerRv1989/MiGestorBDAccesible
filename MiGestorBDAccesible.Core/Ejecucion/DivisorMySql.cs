using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Ejecucion
{
    public record SentenciaMySql(string Texto, int LineaInicial);

    public static partial class DivisorMySql
    {
        [GeneratedRegex(@"^[ \t]*DELIMITER[ \t]+(\S+)[ \t]*$", RegexOptions.IgnoreCase)]
        private static partial Regex LineaDelimitador();

        public static List<SentenciaMySql> Dividir(string script)
        {
            var texto = script.Replace("\r\n", "\n");
            var sentencias = new List<SentenciaMySql>();
            var actual = new StringBuilder();
            var delimitador = ";";
            var linea = 1;
            var lineaInicial = 0;
            var empezada = false;
            var i = 0;

            void Cerrar()
            {
                var contenido = actual.ToString().Trim();

                if (empezada && contenido.Length > 0)
                {
                    sentencias.Add(new SentenciaMySql(contenido, lineaInicial));
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
                        linea += fragmento.Count(c => c == '\n');
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
                if (EmpiezaLinea(texto, i))
                {
                    var finLinea = texto.IndexOf('\n', i);
                    finLinea = finLinea < 0 ? texto.Length : finLinea;

                    if (LineaDelimitador().Match(texto[i..finLinea]) is { Success: true } cambio)
                    {
                        Cerrar();
                        delimitador = cambio.Groups[1].Value;
                        i = finLinea;
                        continue;
                    }
                }

                var c = texto[i];

                if (string.CompareOrdinal(texto, i, delimitador, 0, delimitador.Length) == 0)
                {
                    Cerrar();
                    i += delimitador.Length;
                    continue;
                }

                if ((c == '-' && Siguiente(texto, i) == '-' && (i + 2 >= texto.Length || char.IsWhiteSpace(texto[i + 2]))) || c == '#')
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
                    var ejecutable = Siguiente(texto, i + 1) == '!';
                    Agregar(texto[i..fin], ejecutable);
                    i = fin;
                    continue;
                }

                if (c is '\'' or '"' or '`')
                {
                    var fin = FinCita(texto, i, c);
                    Agregar(texto[i..fin], true);
                    i = fin;
                    continue;
                }

                Agregar(c.ToString(), !char.IsWhiteSpace(c));
                i++;
            }

            Cerrar();
            return sentencias;
        }

        private static bool EmpiezaLinea(string texto, int i) => i == 0 || texto[i - 1] == '\n';

        private static char Siguiente(string texto, int i) => i + 1 < texto.Length ? texto[i + 1] : '\0';

        private static int FinCita(string texto, int inicio, char comilla)
        {
            var i = inicio + 1;

            while (i < texto.Length)
            {
                if (texto[i] == '\\' && comilla != '`')
                {
                    i += 2;
                    continue;
                }

                if (texto[i] == comilla)
                {
                    if (Siguiente(texto, i) == comilla)
                    {
                        i += 2;
                        continue;
                    }

                    return i + 1;
                }

                i++;
            }

            return texto.Length;
        }
    }
}
