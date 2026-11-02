using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Definicion
{
    public class DestinoLlaveModelo
    {
        public TablaModelo Tabla { get; init; } = new();

        public string Columna { get; init; } = "";

        public string TipoDatos { get; init; } = "";

        public bool EsClavePrincipal { get; init; }

        public string Clave => $"{Tabla.NombreCompleto}.{Columna}";

        public string Descripcion =>
            $"{Tabla.NombreCorto()}.{Columna}: {TipoDatos}, {(EsClavePrincipal ? "clave principal" : "valor único")}";
    }

    public class ReferenciaModelo
    {
        public TablaModelo TablaDestino { get; set; } = new();

        public string ColumnaDestino { get; set; } = "";

        public string TipoDestino { get; set; } = "";

        public ReglaReferencial AlEliminar { get; set; }

        public ReglaReferencial AlActualizar { get; set; }

        public ReferenciaModelo Copiar() => new()
        {
            TablaDestino = TablaDestino,
            ColumnaDestino = ColumnaDestino,
            TipoDestino = TipoDestino,
            AlEliminar = AlEliminar,
            AlActualizar = AlActualizar
        };
    }

    public class DefinicionLlaveForaneaModelo
    {
        public TablaModelo TablaOrigen { get; set; } = new();

        public string ColumnaOrigen { get; set; } = "";

        public ReferenciaModelo Referencia { get; set; } = new();

        public bool ComprobarExistentes { get; set; } = true;
    }

    public static class ReglaReferencialExtensiones
    {
        public static string Sql(this ReglaReferencial regla) => regla switch
        {
            ReglaReferencial.Cascada => "CASCADE",
            ReglaReferencial.EstablecerNulo => "SET NULL",
            ReglaReferencial.EstablecerPredeterminado => "SET DEFAULT",
            ReglaReferencial.Restringir => "RESTRICT",
            _ => "NO ACTION"
        };

        public static string Explicacion(this ReglaReferencial regla, bool alBorrar) => regla switch
        {
            ReglaReferencial.Cascada => alBorrar
                ? "En cascada (CASCADE): también se borran las filas que apuntan a ella"
                : "En cascada (CASCADE): las filas que apuntan a ella se actualizan también",
            ReglaReferencial.EstablecerNulo => "Establecer en NULL (SET NULL): las filas que apuntan a ella quedan en NULL",
            ReglaReferencial.EstablecerPredeterminado => "Establecer predeterminado (SET DEFAULT): las filas que apuntan a ella toman su valor predeterminado",
            ReglaReferencial.Restringir => alBorrar
                ? "Restringir (RESTRICT): no se permite borrar si hay filas que apuntan a ella, y se comprueba al momento"
                : "Restringir (RESTRICT): no se permite cambiar la clave si hay filas que apuntan a ella, y se comprueba al momento",
            _ => alBorrar
                ? "Sin acción (NO ACTION): no se permite borrar si hay filas que apuntan a ella"
                : "Sin acción (NO ACTION): no se permite cambiar la clave si hay filas que apuntan a ella"
        };

        public static bool TiposCompatibles(string tipoOrigen, string tipoDestino) =>
            Normalizar(tipoOrigen) == Normalizar(tipoDestino);

        private static string Normalizar(string tipo)
        {
            var texto = tipo.Replace(" ", "").ToLowerInvariant();
            return texto.EndsWith("(7)") && (texto.StartsWith("datetime2") || texto.StartsWith("time") || texto.StartsWith("datetimeoffset"))
                ? texto[..^3]
                : texto;
        }
    }
}
