namespace MiGestorBDAccesible.Core.Estructura
{
    public static class BuscadorObjetosService
    {
        public const int Maximo = 200;

        public const int MinimoCaracteres = 2;

        public static string Categoria(CategoriaBusqueda categoria) => categoria switch
        {
            CategoriaBusqueda.Tablas => "Tablas",
            CategoriaBusqueda.Columnas => "Columnas",
            CategoriaBusqueda.Vistas => "Vistas",
            CategoriaBusqueda.Rutinas => "Procedimientos y funciones",
            CategoriaBusqueda.IndicesRestricciones => "Índices y restricciones",
            CategoriaBusqueda.Disparadores => "Disparadores",
            CategoriaBusqueda.Secuencias => "Secuencias",
            _ => "Todo"
        };

        public static IReadOnlyList<TipoResultadoBusqueda> Tipos(CategoriaBusqueda categoria) => categoria switch
        {
            CategoriaBusqueda.Tablas => [TipoResultadoBusqueda.Tabla],
            CategoriaBusqueda.Columnas => [TipoResultadoBusqueda.Columna],
            CategoriaBusqueda.Vistas => [TipoResultadoBusqueda.Vista],
            CategoriaBusqueda.Rutinas => [TipoResultadoBusqueda.Procedimiento, TipoResultadoBusqueda.Funcion, TipoResultadoBusqueda.Paquete, TipoResultadoBusqueda.Subprograma],
            CategoriaBusqueda.IndicesRestricciones => [TipoResultadoBusqueda.Indice, TipoResultadoBusqueda.ClavePrincipal, TipoResultadoBusqueda.LlaveForanea, TipoResultadoBusqueda.Unica, TipoResultadoBusqueda.Check],
            CategoriaBusqueda.Disparadores => [TipoResultadoBusqueda.Disparador],
            CategoriaBusqueda.Secuencias => [TipoResultadoBusqueda.Secuencia],
            _ => Enum.GetValues<TipoResultadoBusqueda>()
        };

        public static string ListaTipos(CategoriaBusqueda categoria) =>
            string.Join(", ", Tipos(categoria).Select(t => (int)t));

        public static string Escapar(string texto) =>
            texto.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

        public static string Contiene(string texto) => $"%{Escapar(texto)}%";

        public static string EmpiezaPor(string texto) => $"{Escapar(texto)}%";

        public static List<ResultadoBusquedaObjetoModelo> Ordenar(IEnumerable<ResultadoBusquedaObjetoModelo> resultados, string texto) =>
            resultados
                .OrderBy(r => string.Equals(r.Nombre, texto, StringComparison.OrdinalIgnoreCase) ? 0
                    : r.Nombre.StartsWith(texto, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(r => r.Tipo)
                .ThenBy(r => r.Esquema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Tabla?.Nombre ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static string Describir(ResultadoBusquedaObjetoModelo resultado, string disparadoresBase = "de la base")
        {
            var deTabla = resultado.Tabla is null ? "" : $" de la tabla {resultado.Tabla.NombreCompleto}";
            var firma = resultado.Detalle is null ? "" : $"({resultado.Detalle})";

            return resultado.Tipo switch
            {
                TipoResultadoBusqueda.Tabla => $"Tabla {resultado.NombreCompleto}",
                TipoResultadoBusqueda.Columna => $"Columna {resultado.Nombre}{deTabla}{(resultado.Detalle is null ? "" : $", {resultado.Detalle}")}",
                TipoResultadoBusqueda.Vista => $"Vista {resultado.NombreCompleto}",
                TipoResultadoBusqueda.Procedimiento => $"Procedimiento {resultado.NombreCompleto}{firma}",
                TipoResultadoBusqueda.Funcion => $"Función {resultado.NombreCompleto}{firma}",
                TipoResultadoBusqueda.Paquete => $"Paquete {resultado.NombreCompleto}",
                TipoResultadoBusqueda.Subprograma => $"Subprograma {resultado.Nombre} del paquete {resultado.Esquema}.{resultado.Detalle}",
                TipoResultadoBusqueda.Indice => $"Índice {resultado.Nombre}{deTabla}",
                TipoResultadoBusqueda.ClavePrincipal => $"Clave principal {resultado.Nombre}{deTabla}",
                TipoResultadoBusqueda.LlaveForanea => $"Llave foránea {resultado.Nombre}{deTabla}",
                TipoResultadoBusqueda.Unica => $"Restricción UNIQUE {resultado.Nombre}{deTabla}",
                TipoResultadoBusqueda.Check => $"Restricción CHECK {resultado.Nombre}{deTabla}",
                TipoResultadoBusqueda.Disparador => resultado.Tabla is null ? $"Disparador {resultado.Nombre} {disparadoresBase}" : $"Disparador {resultado.Nombre}{deTabla}",
                _ => $"Secuencia {resultado.NombreCompleto}"
            };
        }
    }
}
