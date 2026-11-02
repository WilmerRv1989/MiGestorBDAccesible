using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        public string ScriptCrearIndice(DefinicionIndiceModelo indice)
        {
            var script = new StringBuilder();
            script.Append($"CREATE {(indice.EsUnico ? "UNIQUE " : "")}INDEX {(indice.Concurrente ? "CONCURRENTLY " : "")}{Identificador(NombreNuevo(indice.Nombre, "ix_nuevo"))}");
            script.Append($" ON {NombreTabla(indice.Tabla)}");

            if (indice.Metodo is { Length: > 0 } metodo && metodo != "btree")
            {
                script.Append($" USING {metodo}");
            }

            script.AppendLine();
            script.Append($"    ({string.Join(", ", indice.Columnas.Select(c => $"{Identificador(c.Nombre)}{(c.Descendente ? " DESC" : "")}"))})");

            if (indice.Incluidas.Count > 0)
            {
                script.AppendLine();
                script.Append($"    INCLUDE ({string.Join(", ", indice.Incluidas.Select(Identificador))})");
            }

            if (!string.IsNullOrWhiteSpace(indice.Filtro))
            {
                script.AppendLine();
                script.Append($"    WHERE {indice.Filtro.Trim()}");
            }

            script.Append(';');
            return script.ToString();
        }

        public string ScriptEliminarIndice(TablaModelo tabla, IndiceModelo indice) =>
            $"DROP INDEX {(tabla.Esquema == "public" ? "" : $"{Identificador(tabla.Esquema)}.")}{Identificador(indice.Nombre)};";

        public string ScriptCrearCheck(DefinicionCheckModelo check) =>
            $"""
            ALTER TABLE {NombreTabla(check.Tabla)}
                ADD CONSTRAINT {Identificador(NombreNuevo(check.Nombre, "ck_nuevo"))} CHECK ({(string.IsNullOrWhiteSpace(check.Condicion) ? "condición" : check.Condicion.Trim())}){(check.ComprobarExistentes ? "" : " NOT VALID")};
            """;

        public string ScriptEliminarCheck(TablaModelo tabla, string nombre) =>
            $"ALTER TABLE {NombreTabla(tabla)} DROP CONSTRAINT {Identificador(nombre)};";

        public async Task<Edicion.ISesionEdicion> AbrirSesionEdicionAsync(Conexiones.ConexionActiva conexion, string baseDatos, CancellationToken cancelacion = default) =>
            new Edicion.SesionEdicionAdo(await AbrirAsync(conexion, baseDatos, cancelacion), ex => ex is Npgsql.PostgresException postgres ? TextoError(postgres) : ex.Message);

        private string NombreNuevo(string nombre, string porDefecto) => string.IsNullOrWhiteSpace(nombre) ? porDefecto : NombreGuardado(nombre);
    }
}
