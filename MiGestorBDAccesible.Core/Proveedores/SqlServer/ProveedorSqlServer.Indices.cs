using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        public string ScriptCrearIndice(DefinicionIndiceModelo indice)
        {
            var script = new StringBuilder();
            script.Append($"CREATE {(indice.EsUnico ? "UNIQUE " : "")}{(indice.EsAgrupado ? "CLUSTERED" : "NONCLUSTERED")} INDEX {Delimitar(Nombre(indice.Nombre, "IX_Nuevo"))}");
            script.AppendLine($" ON {NombreCompleto(indice.Tabla)}");
            script.Append($"    ({string.Join(", ", indice.Columnas.Select(c => $"{Delimitar(c.Nombre)} {(c.Descendente ? "DESC" : "ASC")}"))})");

            if (indice.Incluidas.Count > 0)
            {
                script.AppendLine();
                script.Append($"    INCLUDE ({string.Join(", ", indice.Incluidas.Select(Delimitar))})");
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
            $"DROP INDEX {Delimitar(indice.Nombre)} ON {NombreCompleto(tabla)};";

        public string ScriptCrearCheck(DefinicionCheckModelo check) =>
            $"""
            ALTER TABLE {NombreCompleto(check.Tabla)} {(check.ComprobarExistentes ? "WITH CHECK" : "WITH NOCHECK")}
                ADD CONSTRAINT {Delimitar(Nombre(check.Nombre, "CK_Nuevo"))} CHECK ({(string.IsNullOrWhiteSpace(check.Condicion) ? "condición" : check.Condicion.Trim())});
            """;

        public string ScriptEliminarCheck(TablaModelo tabla, string nombre) =>
            $"ALTER TABLE {NombreCompleto(tabla)} DROP CONSTRAINT {Delimitar(nombre)};";

        public async Task<Edicion.ISesionEdicion> AbrirSesionEdicionAsync(Conexiones.ConexionActiva conexion, string baseDatos, CancellationToken cancelacion = default) =>
            new Edicion.SesionEdicionAdo(await AbrirAsync(conexion, baseDatos, cancelacion), ex => ex.Message, contexto: Contexto);

        private static string Nombre(string nombre, string porDefecto) => string.IsNullOrWhiteSpace(nombre) ? porDefecto : nombre.Trim();
    }
}
