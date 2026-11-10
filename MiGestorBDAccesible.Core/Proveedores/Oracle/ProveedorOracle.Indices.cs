using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        public string ScriptCrearIndice(DefinicionIndiceModelo indice) =>
            $"""
            CREATE {(indice.EsUnico ? "UNIQUE " : "")}INDEX {Identificador(indice.Tabla.Esquema)}.{Identificador(NombreNuevo(indice.Nombre, "IX_NUEVO"))}
                ON {Identificador(indice.Tabla.Esquema)}.{Identificador(indice.Tabla.Nombre)} ({string.Join(", ", indice.Columnas.Select(c => $"{Identificador(c.Nombre)}{(c.Descendente ? " DESC" : "")}"))});
            """;

        public string ScriptEliminarIndice(TablaModelo tabla, IndiceModelo indice) =>
            $"DROP INDEX {Identificador(tabla.Esquema)}.{Identificador(indice.Nombre)};";

        public string ScriptCrearCheck(DefinicionCheckModelo check) =>
            $"""
            ALTER TABLE {Identificador(check.Tabla.Esquema)}.{Identificador(check.Tabla.Nombre)}
                ADD CONSTRAINT {Identificador(NombreNuevo(check.Nombre, "CK_NUEVO"))} CHECK ({(string.IsNullOrWhiteSpace(check.Condicion) ? "condición" : check.Condicion.Trim())}){(check.ComprobarExistentes ? "" : " ENABLE NOVALIDATE")};
            """;

        public string ScriptEliminarCheck(TablaModelo tabla, string nombre) =>
            $"ALTER TABLE {Identificador(tabla.Esquema)}.{Identificador(tabla.Nombre)} DROP CONSTRAINT {Identificador(nombre)};";

        public async Task<Edicion.ISesionEdicion> AbrirSesionEdicionAsync(Conexiones.ConexionActiva conexion, string baseDatos, CancellationToken cancelacion = default) =>
            new Edicion.SesionEdicionAdo(await AbrirAsync(conexion, cancelacion), ex => LimpiarMensaje(ex.Message), contexto: Contexto);

        private string NombreNuevo(string nombre, string porDefecto) => string.IsNullOrWhiteSpace(nombre) ? porDefecto : NombreGuardado(nombre);
    }
}
