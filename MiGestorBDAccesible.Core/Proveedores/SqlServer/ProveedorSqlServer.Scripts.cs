using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        public string BaseDatosAdministracion => "master";

        public async Task<string> GenerarScriptAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            TipoScript tipo,
            CancellationToken cancelacion = default)
        {
            var detalle = await ObtenerDetalleTablaAsync(conexion, baseDatos, tabla, cancelacion);
            var script = new StringBuilder();

            script.AppendLine($"USE {Delimitar(baseDatos)};");
            script.AppendLine("GO");
            script.AppendLine();

            switch (tipo)
            {
                case TipoScript.Crear:
                    EscribirCrear(script, detalle);
                    break;
                case TipoScript.Seleccionar:
                    EscribirSeleccionar(script, detalle, null);
                    break;
                case TipoScript.SeleccionarPrimeras:
                    EscribirSeleccionar(script, detalle, 100);
                    break;
                case TipoScript.Insertar:
                    EscribirInsertar(script, detalle);
                    break;
                case TipoScript.Actualizar:
                    EscribirActualizar(script, detalle);
                    break;
                case TipoScript.Eliminar:
                    EscribirEliminar(script, detalle);
                    break;
                case TipoScript.EliminarTabla:
                    EscribirEliminarTabla(script, detalle);
                    break;
            }

            return script.ToString().TrimEnd() + Environment.NewLine;
        }

        public string ScriptCrearBaseDatos(string nombre) => $"CREATE DATABASE {Delimitar(nombre)};";

        public string ScriptEliminarBaseDatos(string nombre) =>
            $"""
            USE master;
            ALTER DATABASE {Delimitar(nombre)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE {Delimitar(nombre)};
            """;

        public string ScriptEliminarTabla(TablaModelo tabla) => $"DROP TABLE {NombreCompleto(tabla)};";

        public void ReiniciarConexiones() => SqlConnection.ClearAllPools();

        private static void EscribirCrear(StringBuilder script, DetalleTablaModelo detalle)
        {
            var lineas = new List<string>();

            foreach (var columna in detalle.Columnas)
            {
                if (columna.EsCalculada)
                {
                    lineas.Add($"{Delimitar(columna.Nombre)} AS ({NarradorTablaService.QuitarParentesisExternos(columna.Formula!)}){(columna.EsPersistente ? " PERSISTED" : "")}");
                    continue;
                }

                var linea = new StringBuilder($"{Delimitar(columna.Nombre)} {columna.TipoDatos}");

                if (columna.EsIdentidad)
                {
                    linea.Append($" IDENTITY({columna.SemillaIdentidad}, {columna.IncrementoIdentidad})");
                }

                linea.Append(columna.PermiteNulos ? " NULL" : " NOT NULL");

                if (columna.ValorPredeterminado is not null)
                {
                    linea.Append($" DEFAULT ({NarradorTablaService.QuitarParentesisExternos(columna.ValorPredeterminado)})");
                }

                lineas.Add(linea.ToString());
            }

            foreach (var indice in detalle.Indices.Where(i => i.EsClavePrincipal || i.EsRestriccionUnica))
            {
                var tipo = indice.EsClavePrincipal ? "PRIMARY KEY" : "UNIQUE";
                var agrupacion = indice.EsAgrupado ? "CLUSTERED" : "NONCLUSTERED";
                lineas.Add($"CONSTRAINT {Delimitar(indice.Nombre)} {tipo} {agrupacion} ({ColumnasIndice(indice)})");
            }

            foreach (var restriccion in detalle.Restricciones)
            {
                lineas.Add($"CONSTRAINT {Delimitar(restriccion.Nombre)} CHECK ({NarradorTablaService.QuitarParentesisExternos(restriccion.Definicion)})");
            }

            script.AppendLine($"CREATE TABLE {NombreCompleto(detalle.Tabla)} (");
            script.AppendLine(string.Join($",{Environment.NewLine}", lineas.Select(l => $"    {l}")));
            script.AppendLine(");");
            script.AppendLine("GO");

            foreach (var llave in detalle.LlavesForaneas)
            {
                script.AppendLine();
                script.Append($"ALTER TABLE {NombreCompleto(detalle.Tabla)}");
                script.AppendLine(llave.Habilitada ? "" : " WITH NOCHECK");
                script.Append($"    ADD CONSTRAINT {Delimitar(llave.Nombre)} FOREIGN KEY ({string.Join(", ", llave.ColumnasOrigen.Select(Delimitar))})");
                script.AppendLine();
                script.Append($"    REFERENCES {NombreCompleto(llave.TablaDestino)} ({string.Join(", ", llave.ColumnasDestino.Select(Delimitar))})");

                if (llave.AlEliminar != ReglaReferencial.SinAccion)
                {
                    script.AppendLine();
                    script.Append($"    ON DELETE {TextoRegla(llave.AlEliminar)}");
                }

                if (llave.AlActualizar != ReglaReferencial.SinAccion)
                {
                    script.AppendLine();
                    script.Append($"    ON UPDATE {TextoRegla(llave.AlActualizar)}");
                }

                script.AppendLine(";");
                script.AppendLine("GO");
            }

            foreach (var indice in detalle.Indices.Where(i => !i.EsClavePrincipal && !i.EsRestriccionUnica))
            {
                script.AppendLine();
                script.Append($"CREATE {(indice.EsUnico ? "UNIQUE " : "")}{(indice.EsAgrupado ? "CLUSTERED" : "NONCLUSTERED")} INDEX {Delimitar(indice.Nombre)}");
                script.Append($" ON {NombreCompleto(detalle.Tabla)} ({ColumnasIndice(indice)})");

                var incluidas = indice.Columnas.Where(c => c.Incluida).Select(c => Delimitar(c.Nombre)).ToList();

                if (incluidas.Count > 0)
                {
                    script.Append($" INCLUDE ({string.Join(", ", incluidas)})");
                }

                script.AppendLine(";");
                script.AppendLine("GO");
            }
        }

        private static void EscribirSeleccionar(StringBuilder script, DetalleTablaModelo detalle, int? primeras)
        {
            script.AppendLine(primeras is int cantidad ? $"SELECT TOP ({cantidad})" : "SELECT");
            script.AppendLine(string.Join($",{Environment.NewLine}", detalle.Columnas.Select(c => $"    {Delimitar(c.Nombre)}")));
            script.AppendLine($"FROM {NombreCompleto(detalle.Tabla)};");
        }

        private static void EscribirInsertar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var columnas = detalle.Columnas.Where(c => !c.EsIdentidad && !c.EsCalculada).ToList();

            if (columnas.Count == 0)
            {
                script.AppendLine($"INSERT INTO {NombreCompleto(detalle.Tabla)} DEFAULT VALUES;");
                return;
            }

            script.AppendLine("-- Cambia cada valor de ejemplo por el dato real.");
            script.AppendLine($"INSERT INTO {NombreCompleto(detalle.Tabla)} ({string.Join(", ", columnas.Select(c => Delimitar(c.Nombre)))})");
            script.AppendLine("VALUES (");

            for (var i = 0; i < columnas.Count; i++)
            {
                var coma = i < columnas.Count - 1 ? "," : "";
                script.AppendLine($"    {ValorEjemplo(columnas[i])}{coma}  -- {ComentarioColumna(columnas[i], detalle)}");
            }

            script.AppendLine(");");
        }

        private static void EscribirActualizar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];
            var columnas = detalle.Columnas
                .Where(c => !c.EsIdentidad && !c.EsCalculada && !clave.Contains(c.Nombre, StringComparer.OrdinalIgnoreCase))
                .ToList();

            script.AppendLine("-- Deja solo las columnas que quieres cambiar y escribe sus valores nuevos.");
            script.AppendLine($"UPDATE {NombreCompleto(detalle.Tabla)}");
            script.AppendLine("SET");

            for (var i = 0; i < columnas.Count; i++)
            {
                var coma = i < columnas.Count - 1 ? "," : "";
                script.AppendLine($"    {Delimitar(columnas[i].Nombre)} = {ValorEjemplo(columnas[i])}{coma}  -- {ComentarioColumna(columnas[i], detalle)}");
            }

            EscribirWhere(script, detalle, "cambiar");
        }

        private static void EscribirEliminar(StringBuilder script, DetalleTablaModelo detalle)
        {
            script.AppendLine($"DELETE FROM {NombreCompleto(detalle.Tabla)}");
            EscribirWhere(script, detalle, "borrar");

            if (detalle.Referencias.Count > 0)
            {
                var tablas = NarradorTablaService.Unir(detalle.Referencias.Select(r => r.TablaOrigen.NombreCorto()).Distinct());
                script.AppendLine($"-- Atención: {tablas} apunta a esta tabla. Según su regla ON DELETE, el borrado se rechaza o arrastra filas relacionadas.");
            }
        }

        private static void EscribirWhere(StringBuilder script, DetalleTablaModelo detalle, string verbo)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];

            if (clave.Count == 0)
            {
                script.AppendLine($"WHERE 1 = 0;  -- Esta tabla no tiene clave principal. Escribe aquí la condición de las filas que quieres {verbo}.");
                return;
            }

            var condiciones = clave.Select(c =>
            {
                var columna = detalle.Columnas.First(x => x.Nombre.Equals(c, StringComparison.OrdinalIgnoreCase));
                return $"{Delimitar(c)} = {ValorEjemplo(columna)}";
            });

            script.AppendLine($"WHERE {string.Join(" AND ", condiciones)};  -- Pon el valor de la clave de la fila que quieres {verbo}. Sin WHERE se afectan todas las filas.");
        }

        private static void EscribirEliminarTabla(StringBuilder script, DetalleTablaModelo detalle)
        {
            if (detalle.Referencias.Count > 0)
            {
                foreach (var referencia in detalle.Referencias)
                {
                    script.AppendLine($"-- Atención: {referencia.TablaOrigen.NombreCorto()} apunta a esta tabla con la llave {referencia.Nombre}. Hay que quitar esa llave antes:");
                    script.AppendLine($"-- ALTER TABLE {NombreCompleto(referencia.TablaOrigen)} DROP CONSTRAINT {Delimitar(referencia.Nombre)};");
                }

                script.AppendLine();
            }

            script.AppendLine($"DROP TABLE {NombreCompleto(detalle.Tabla)};");
        }

        private static string ComentarioColumna(ColumnaModelo columna, DetalleTablaModelo detalle)
        {
            var partes = new List<string> { columna.TipoDatos, columna.PermiteNulos ? "opcional" : "obligatoria" };

            foreach (var llave in detalle.LlavesForaneas.Where(l => l.ColumnasOrigen.Contains(columna.Nombre, StringComparer.OrdinalIgnoreCase)))
            {
                var indice = llave.ColumnasOrigen.FindIndex(c => c.Equals(columna.Nombre, StringComparison.OrdinalIgnoreCase));
                partes.Add($"apunta a {llave.TablaDestino.NombreCorto()}.{llave.ColumnasDestino[indice]}");
            }

            if (columna.ValorPredeterminado is not null)
            {
                partes.Add($"predeterminado {NarradorTablaService.QuitarParentesisExternos(columna.ValorPredeterminado)}");
            }

            return $"{columna.Nombre}: {string.Join(", ", partes)}";
        }

        private static string ValorEjemplo(ColumnaModelo columna) =>
            columna.TipoDatos.Split('(')[0].ToLowerInvariant() switch
            {
                "nvarchar" or "nchar" or "ntext" => "N''",
                "varchar" or "char" or "text" => "''",
                "date" => "'2026-01-01'",
                "datetime" or "datetime2" or "smalldatetime" => "SYSDATETIME()",
                "datetimeoffset" => "SYSDATETIMEOFFSET()",
                "time" => "'08:00'",
                "uniqueidentifier" => "NEWID()",
                "varbinary" or "binary" or "image" => "0x",
                _ => "0"
            };

        private static string ColumnasIndice(IndiceModelo indice) =>
            string.Join(", ", indice.Columnas.Where(c => !c.Incluida)
                .Select(c => $"{Delimitar(c.Nombre)} {(c.Descendente ? "DESC" : "ASC")}"));

        private static string TextoRegla(ReglaReferencial regla) => regla switch
        {
            ReglaReferencial.Cascada => "CASCADE",
            ReglaReferencial.EstablecerNulo => "SET NULL",
            ReglaReferencial.EstablecerPredeterminado => "SET DEFAULT",
            _ => "NO ACTION"
        };

        private static string NombreCompleto(TablaModelo tabla) => $"{Delimitar(tabla.Esquema)}.{Delimitar(tabla.Nombre)}";
    }
}
