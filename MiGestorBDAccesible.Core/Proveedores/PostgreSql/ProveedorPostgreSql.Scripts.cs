using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private static readonly HashSet<string> _palabrasReservadas = new(StringComparer.Ordinal)
        {
            "all", "analyse", "analyze", "and", "any", "array", "as", "asc", "asymmetric", "authorization", "binary",
            "both", "case", "cast", "check", "collate", "collation", "column", "concurrently", "constraint", "create",
            "cross", "current_catalog", "current_date", "current_role", "current_schema", "current_time",
            "current_timestamp", "current_user", "default", "deferrable", "desc", "distinct", "do", "else", "end",
            "except", "false", "fetch", "for", "foreign", "freeze", "from", "full", "grant", "group", "having", "ilike",
            "in", "initially", "inner", "intersect", "into", "is", "isnull", "join", "lateral", "leading", "left", "like",
            "limit", "localtime", "localtimestamp", "natural", "not", "notnull", "null", "offset", "on", "only", "or",
            "order", "outer", "overlaps", "placing", "primary", "references", "returning", "right", "select",
            "session_user", "similar", "some", "symmetric", "system_user", "table", "tablesample", "then", "to",
            "trailing", "true", "union", "unique", "user", "using", "variadic", "verbose", "when", "where", "window", "with"
        };

        private static readonly Regex _nombreSimple = new(@"^[a-z_][a-z0-9_$]*$");

        private static readonly Regex _secuenciaSerial = new(@"^nextval\('[^']*_seq'::regclass\)$");

        public async Task<string> GenerarScriptAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            TipoScript tipo,
            CancellationToken cancelacion = default)
        {
            var detalle = await ObtenerDetalleTablaAsync(conexion, baseDatos, tabla, cancelacion);
            var script = new StringBuilder();

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

        public string ScriptCrearBaseDatos(string nombre) => $"CREATE DATABASE {Identificador(nombre)};";

        public string ScriptEliminarBaseDatos(string nombre) => $"DROP DATABASE {Identificador(nombre)} WITH (FORCE);";

        public string ScriptEliminarTabla(TablaModelo tabla) => $"DROP TABLE {NombreTabla(tabla)};";

        public static string Identificador(string nombre) =>
            _nombreSimple.IsMatch(nombre) && !_palabrasReservadas.Contains(nombre) ? nombre : Delimitar(nombre);

        private static string NombreTabla(TablaModelo tabla) =>
            tabla.Esquema == "public" ? Identificador(tabla.Nombre) : $"{Identificador(tabla.Esquema)}.{Identificador(tabla.Nombre)}";

        private static string ColumnaIndice(ColumnaIndiceModelo columna) =>
            (columna.Nombre.Contains('(') ? columna.Nombre : Identificador(columna.Nombre)) + (columna.Descendente ? " DESC" : "");

        private static void EscribirCrear(StringBuilder script, DetalleTablaModelo detalle)
        {
            var tabla = NombreTabla(detalle.Tabla);
            var lineas = new List<string>();

            foreach (var columna in detalle.Columnas)
            {
                var nombre = Identificador(columna.Nombre);

                if (columna.EsCalculada)
                {
                    lineas.Add($"{nombre} {columna.TipoDatos} GENERATED ALWAYS AS ({NarradorTablaService.QuitarParentesisExternos(columna.Formula!)}) {(columna.EsPersistente ? "STORED" : "VIRTUAL")}");
                    continue;
                }

                if (columna.ValorPredeterminado is { } predeterminado && _secuenciaSerial.IsMatch(predeterminado) && TipoSerial(columna.TipoDatos) is { } serial)
                {
                    lineas.Add($"{nombre} {serial}{(columna.PermiteNulos ? "" : " NOT NULL")}");
                    continue;
                }

                var linea = new StringBuilder($"{nombre} {columna.TipoDatos}");

                if (columna.EsIdentidad)
                {
                    linea.Append(columna.IdentidadSiempre == false ? " GENERATED BY DEFAULT AS IDENTITY" : " GENERATED ALWAYS AS IDENTITY");

                    if ((columna.SemillaIdentidad ?? 1) != 1 || (columna.IncrementoIdentidad ?? 1) != 1)
                    {
                        linea.Append($" (START WITH {columna.SemillaIdentidad} INCREMENT BY {columna.IncrementoIdentidad})");
                    }
                }
                else
                {
                    if (columna.ValorPredeterminado is not null)
                    {
                        linea.Append($" DEFAULT {columna.ValorPredeterminado}");
                    }

                    if (!columna.PermiteNulos)
                    {
                        linea.Append(" NOT NULL");
                    }
                }

                lineas.Add(linea.ToString());
            }

            foreach (var indice in detalle.Indices.Where(i => i.EsClavePrincipal || i.EsRestriccionUnica))
            {
                var tipo = indice.EsClavePrincipal ? "PRIMARY KEY" : "UNIQUE";
                var incluidas = indice.Columnas.Where(c => c.Incluida).Select(c => Identificador(c.Nombre)).ToList();
                lineas.Add($"CONSTRAINT {Identificador(indice.Nombre)} {tipo} ({string.Join(", ", indice.ColumnasClave.Select(Identificador))})"
                    + (incluidas.Count > 0 ? $" INCLUDE ({string.Join(", ", incluidas)})" : ""));
            }

            foreach (var restriccion in detalle.Restricciones)
            {
                lineas.Add($"CONSTRAINT {Identificador(restriccion.Nombre)} CHECK ({restriccion.Definicion})");
            }

            script.AppendLine($"CREATE TABLE {tabla} (");
            script.AppendLine(string.Join($",{Environment.NewLine}", lineas.Select(l => $"    {l}")));
            script.AppendLine(");");

            foreach (var llave in detalle.LlavesForaneas)
            {
                script.AppendLine();
                script.AppendLine($"ALTER TABLE {tabla}");
                script.AppendLine($"    ADD CONSTRAINT {Identificador(llave.Nombre)} FOREIGN KEY ({string.Join(", ", llave.ColumnasOrigen.Select(Identificador))})");
                script.Append($"    REFERENCES {NombreTabla(llave.TablaDestino)} ({string.Join(", ", llave.ColumnasDestino.Select(Identificador))})");

                if (llave.AlEliminar != ReglaReferencial.SinAccion)
                {
                    script.Append($"{Environment.NewLine}    ON DELETE {llave.AlEliminar.Sql()}");
                }

                if (llave.AlActualizar != ReglaReferencial.SinAccion)
                {
                    script.Append($"{Environment.NewLine}    ON UPDATE {llave.AlActualizar.Sql()}");
                }

                script.AppendLine(llave.Habilitada ? ";" : $"{Environment.NewLine}    NOT VALID;");
            }

            foreach (var indice in detalle.Indices.Where(i => !i.EsClavePrincipal && !i.EsRestriccionUnica))
            {
                var metodo = indice.Metodo is { } nombreMetodo && nombreMetodo != "btree" ? $" USING {nombreMetodo}" : "";
                var claves = indice.Columnas.Where(c => !c.Incluida).Select(ColumnaIndice);
                var incluidas = indice.Columnas.Where(c => c.Incluida).Select(c => Identificador(c.Nombre)).ToList();

                script.AppendLine();
                script.Append($"CREATE {(indice.EsUnico ? "UNIQUE " : "")}INDEX {Identificador(indice.Nombre)} ON {tabla}{metodo} ({string.Join(", ", claves)})");

                if (incluidas.Count > 0)
                {
                    script.Append($" INCLUDE ({string.Join(", ", incluidas)})");
                }

                script.AppendLine(";");
            }
        }

        private static string? TipoSerial(string tipo) => tipo switch
        {
            "integer" => "serial",
            "bigint" => "bigserial",
            "smallint" => "smallserial",
            _ => null
        };

        private static void EscribirSeleccionar(StringBuilder script, DetalleTablaModelo detalle, int? primeras)
        {
            script.AppendLine("SELECT");
            script.AppendLine(string.Join($",{Environment.NewLine}", detalle.Columnas.Select(c => $"    {Identificador(c.Nombre)}")));

            if (primeras is int cantidad)
            {
                script.AppendLine($"FROM {NombreTabla(detalle.Tabla)}");
                script.AppendLine($"LIMIT {cantidad};");
            }
            else
            {
                script.AppendLine($"FROM {NombreTabla(detalle.Tabla)};");
            }
        }

        private static bool SeEscribe(ColumnaModelo columna) =>
            !columna.EsCalculada
            && !(columna.EsIdentidad && columna.IdentidadSiempre != false)
            && !(columna.ValorPredeterminado is { } predeterminado && _secuenciaSerial.IsMatch(predeterminado));

        private static void EscribirInsertar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var columnas = detalle.Columnas.Where(c => SeEscribe(c) && !c.EsIdentidad).ToList();
            var tabla = NombreTabla(detalle.Tabla);

            if (columnas.Count == 0)
            {
                script.AppendLine($"INSERT INTO {tabla} DEFAULT VALUES;");
                return;
            }

            script.AppendLine("-- Cambia cada valor de ejemplo por el dato real.");
            script.AppendLine($"INSERT INTO {tabla} ({string.Join(", ", columnas.Select(c => Identificador(c.Nombre)))})");
            script.AppendLine("VALUES (");

            for (var i = 0; i < columnas.Count; i++)
            {
                var coma = i < columnas.Count - 1 ? "," : "";
                script.AppendLine($"    {ValorEjemplo(columnas[i])}{coma}  -- {ComentarioColumna(columnas[i], detalle)}");
            }

            script.AppendLine(")");

            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];
            script.AppendLine(clave.Count > 0 ? $"RETURNING {string.Join(", ", clave.Select(Identificador))};  -- Devuelve la clave de la fila nueva." : ";");
            script.AppendLine("-- Se confirma solo. Para poder deshacerlo, ejecútalo después de BEGIN y termina con COMMIT o ROLLBACK.");
        }

        private static void EscribirActualizar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];
            var columnas = detalle.Columnas
                .Where(c => SeEscribe(c) && !c.EsIdentidad && !clave.Contains(c.Nombre, StringComparer.Ordinal))
                .ToList();

            script.AppendLine("-- Deja solo las columnas que quieres cambiar y escribe sus valores nuevos.");
            script.AppendLine($"UPDATE {NombreTabla(detalle.Tabla)}");
            script.AppendLine("SET");

            for (var i = 0; i < columnas.Count; i++)
            {
                var coma = i < columnas.Count - 1 ? "," : "";
                script.AppendLine($"    {Identificador(columnas[i].Nombre)} = {ValorEjemplo(columnas[i])}{coma}  -- {ComentarioColumna(columnas[i], detalle)}");
            }

            EscribirWhere(script, detalle, "cambiar");
            script.AppendLine("-- Se confirma solo. Para poder deshacerlo, ejecútalo después de BEGIN y termina con COMMIT o ROLLBACK.");
        }

        private static void EscribirEliminar(StringBuilder script, DetalleTablaModelo detalle)
        {
            script.AppendLine($"DELETE FROM {NombreTabla(detalle.Tabla)}");
            EscribirWhere(script, detalle, "borrar");

            if (detalle.Referencias.Count > 0)
            {
                var tablas = NarradorTablaService.Unir(detalle.Referencias.Select(r => r.TablaOrigen.NombreCorto()).Distinct());
                script.AppendLine($"-- Atención: {tablas} apunta a esta tabla. Según su regla ON DELETE, el borrado se rechaza o arrastra filas relacionadas.");
            }

            script.AppendLine("-- Se confirma solo. Para poder deshacerlo, ejecútalo después de BEGIN y termina con COMMIT o ROLLBACK.");
        }

        private static void EscribirWhere(StringBuilder script, DetalleTablaModelo detalle, string verbo)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];

            if (clave.Count == 0)
            {
                script.AppendLine($"WHERE false;  -- Esta tabla no tiene clave principal. Escribe aquí la condición de las filas que quieres {verbo}.");
                return;
            }

            var condiciones = clave.Select(c =>
            {
                var columna = detalle.Columnas.First(x => x.Nombre == c);
                return $"{Identificador(c)} = {ValorEjemplo(columna)}";
            });

            script.AppendLine($"WHERE {string.Join(" AND ", condiciones)};  -- Pon el valor de la clave de la fila que quieres {verbo}. Sin WHERE se afectan todas las filas.");
        }

        private static void EscribirEliminarTabla(StringBuilder script, DetalleTablaModelo detalle)
        {
            var tabla = NombreTabla(detalle.Tabla);

            foreach (var referencia in detalle.Referencias)
            {
                script.AppendLine($"-- Atención: {referencia.TablaOrigen.NombreCorto()} apunta a esta tabla con la llave {referencia.Nombre}. Hay que quitar esa llave antes:");
                script.AppendLine($"-- ALTER TABLE {NombreTabla(referencia.TablaOrigen)} DROP CONSTRAINT {Identificador(referencia.Nombre)};");
            }

            if (detalle.Referencias.Count > 0)
            {
                script.AppendLine("-- O bien agregar CASCADE al final del DROP TABLE, que quita esas llaves y las vistas que dependen de la tabla.");
                script.AppendLine();
            }

            script.AppendLine("-- PostgreSQL no tiene papelera: la tabla se borra para siempre.");
            script.AppendLine("-- Si lo ejecutas después de BEGIN, todavía puedes deshacerlo con ROLLBACK, porque en PostgreSQL el DDL es transaccional.");
            script.AppendLine($"DROP TABLE {tabla};");
        }

        private static string ComentarioColumna(ColumnaModelo columna, DetalleTablaModelo detalle)
        {
            var partes = new List<string> { columna.TipoDatos, columna.PermiteNulos ? "opcional" : "obligatoria" };

            foreach (var llave in detalle.LlavesForaneas.Where(l => l.ColumnasOrigen.Contains(columna.Nombre)))
            {
                var indice = llave.ColumnasOrigen.IndexOf(columna.Nombre);
                partes.Add($"apunta a {llave.TablaDestino.NombreCorto()}.{llave.ColumnasDestino[indice]}");
            }

            if (columna.ValorPredeterminado is not null)
            {
                partes.Add($"predeterminado {columna.ValorPredeterminado}");
            }

            return $"{columna.Nombre}: {string.Join(", ", partes)}";
        }

        private static string ValorEjemplo(ColumnaModelo columna)
        {
            var tipo = columna.TipoDatos.Split('(')[0].Trim();

            return tipo switch
            {
                _ when tipo.EndsWith("[]", StringComparison.Ordinal) => "'{}'",
                "varchar" or "char" or "text" or "citext" or "name" => "''",
                "date" => "DATE '2026-01-01'",
                "timestamp" or "timestamptz" => "now()",
                "time" or "timetz" => "TIME '08:00'",
                "interval" => "INTERVAL '1 day'",
                "boolean" => "true",
                "uuid" => "gen_random_uuid()",
                "json" or "jsonb" => "'{}'",
                "bytea" => "'\\x'",
                _ => "0"
            };
        }
    }
}
