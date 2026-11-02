namespace MiGestorBDAccesible.Core.Trabajo
{
    public class BusquedaModelo
    {
        public bool Abierta { get; set; }

        public string Texto { get; set; } = "";

        public string Reemplazo { get; set; } = "";

        public bool CoincidirMayusculas { get; set; }

        public bool PalabraCompleta { get; set; }
    }

    public record CoincidenciaModelo(int Inicio, int Largo)
    {
        public int Fin => Inicio + Largo;
    }

    public record ResultadoBusquedaModelo(CoincidenciaModelo Coincidencia, int Indice, int Total, bool DioLaVuelta);

    public static class BuscadorTextoService
    {
        public static List<CoincidenciaModelo> Coincidencias(string texto, BusquedaModelo busqueda)
        {
            var coincidencias = new List<CoincidenciaModelo>();

            if (busqueda.Texto.Length == 0)
            {
                return coincidencias;
            }

            var comparacion = busqueda.CoincidirMayusculas ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var posicion = 0;

            while (posicion <= texto.Length - busqueda.Texto.Length)
            {
                var inicio = texto.IndexOf(busqueda.Texto, posicion, comparacion);

                if (inicio < 0)
                {
                    break;
                }

                var fin = inicio + busqueda.Texto.Length;

                if (!busqueda.PalabraCompleta || (!EsDePalabra(texto, inicio - 1) && !EsDePalabra(texto, fin)))
                {
                    coincidencias.Add(new CoincidenciaModelo(inicio, busqueda.Texto.Length));
                    posicion = fin;
                }
                else
                {
                    posicion = inicio + 1;
                }
            }

            return coincidencias;
        }

        private static bool EsDePalabra(string texto, int indice) =>
            indice >= 0 && indice < texto.Length && (char.IsLetterOrDigit(texto[indice]) || texto[indice] == '_');

        public static ResultadoBusquedaModelo? Buscar(string texto, BusquedaModelo busqueda, int inicioSeleccion, int finSeleccion, bool haciaAtras)
        {
            var coincidencias = Coincidencias(texto, busqueda);

            if (coincidencias.Count == 0)
            {
                return null;
            }

            var actual = coincidencias.FindIndex(c => c.Inicio == inicioSeleccion && c.Fin == finSeleccion);
            int indice;
            var dioLaVuelta = false;

            if (haciaAtras)
            {
                indice = actual >= 0 ? actual - 1 : coincidencias.FindLastIndex(c => c.Inicio < inicioSeleccion);

                if (indice < 0)
                {
                    indice = coincidencias.Count - 1;
                    dioLaVuelta = true;
                }
            }
            else
            {
                indice = actual >= 0 ? actual + 1 : coincidencias.FindIndex(c => c.Inicio >= inicioSeleccion);

                if (indice < 0 || indice >= coincidencias.Count)
                {
                    indice = 0;
                    dioLaVuelta = true;
                }
            }

            if (coincidencias.Count == 1)
            {
                dioLaVuelta = false;
            }

            return new ResultadoBusquedaModelo(coincidencias[indice], indice + 1, coincidencias.Count, dioLaVuelta);
        }

        public static (string Texto, int Cantidad) ReemplazarTodo(string texto, BusquedaModelo busqueda)
        {
            var coincidencias = Coincidencias(texto, busqueda);

            for (var i = coincidencias.Count - 1; i >= 0; i--)
            {
                texto = texto.Remove(coincidencias[i].Inicio, coincidencias[i].Largo).Insert(coincidencias[i].Inicio, busqueda.Reemplazo);
            }

            return (texto, coincidencias.Count);
        }

        public static (int Numero, string Texto) Linea(string texto, int posicion)
        {
            posicion = Math.Clamp(posicion, 0, texto.Length);
            var inicio = posicion == 0 ? 0 : texto.LastIndexOf('\n', posicion - 1) + 1;
            var fin = texto.IndexOf('\n', posicion);
            var numero = 1 + texto.AsSpan(0, inicio).Count('\n');

            return (numero, texto[inicio..(fin < 0 ? texto.Length : fin)].Trim());
        }

        public static string Anuncio(string texto, ResultadoBusquedaModelo resultado)
        {
            var (numero, linea) = Linea(texto, resultado.Coincidencia.Inicio);
            var vuelta = resultado.DioLaVuelta ? "Volviendo al principio. " : "";

            if (resultado.DioLaVuelta && resultado.Indice != 1)
            {
                vuelta = "Volviendo al final. ";
            }

            var cierre = linea.Length > 0 && ".;:,".Contains(linea[^1]) ? " " : ". ";
            return $"{vuelta}Línea {numero}: {linea}{cierre}Coincidencia {resultado.Indice} de {resultado.Total}.";
        }
    }
}
