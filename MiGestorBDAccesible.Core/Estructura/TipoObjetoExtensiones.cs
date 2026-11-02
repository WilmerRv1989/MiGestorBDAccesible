namespace MiGestorBDAccesible.Core.Estructura
{
    public static class TipoObjetoExtensiones
    {
        public static string Singular(this TipoObjeto tipo) => tipo switch
        {
            TipoObjeto.Vista => "vista",
            TipoObjeto.Procedimiento => "procedimiento",
            TipoObjeto.Funcion => "función",
            TipoObjeto.Paquete => "paquete",
            TipoObjeto.Secuencia => "secuencia",
            _ => "disparador"
        };

        public static string Plural(this TipoObjeto tipo) => tipo switch
        {
            TipoObjeto.Vista => "Vistas",
            TipoObjeto.Procedimiento => "Procedimientos",
            TipoObjeto.Funcion => "Funciones",
            TipoObjeto.Paquete => "Paquetes",
            TipoObjeto.Secuencia => "Secuencias",
            _ => "Disparadores"
        };

        public static string ConArticulo(this TipoObjeto tipo) => tipo switch
        {
            TipoObjeto.Vista or TipoObjeto.Funcion or TipoObjeto.Secuencia => $"la {tipo.Singular()}",
            _ => $"el {tipo.Singular()}"
        };

        public static string DeArticulo(this TipoObjeto tipo) =>
            tipo.ConArticulo() is var articulo && articulo.StartsWith("el ") ? $"del {articulo[3..]}" : $"de {articulo}";

        public static List<string> TemasAyuda(this TipoObjeto tipo) => tipo switch
        {
            TipoObjeto.Vista => ["concepto:vistas"],
            TipoObjeto.Procedimiento => ["concepto:procedimientos", "plsql:procedimientos", "plpgsql:procedimientos"],
            TipoObjeto.Funcion => ["concepto:funciones", "plsql:procedimientos", "plpgsql:funciones"],
            TipoObjeto.Paquete => ["plsql:paquetes"],
            TipoObjeto.Secuencia => ["concepto:secuencias"],
            _ => ["concepto:disparadores", "plsql:disparadores", "plpgsql:disparadores"]
        };
    }
}
