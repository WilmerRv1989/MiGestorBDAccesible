using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private static readonly System.Text.RegularExpressions.Regex _tablaDelDisparador = new(
            @"^(?<antes>.*?\bTRIGGER\s+(\[[^\]]+\]|[\w.]+)(\.(\[[^\]]+\]|\w+))?\s+ON\s+)(\[[^\]]+\]|[\w#]+)(\.(\[[^\]]+\]|[\w#]+))?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly HashSet<string> _tiposTextoLongitud = new(StringComparer.OrdinalIgnoreCase) { "nvarchar", "varchar", "nchar", "char" };

        public DialectoSqlModelo Dialecto { get; } = new()
        {
            Columna = Delimitar,
            Tabla = NombreCompleto,
            Longitud = c => $"LEN({c})",
            NoConvertible = (c, tipo) => $"TRY_CONVERT({tipo}, {c}) IS NULL",
            Primeras = consulta => consulta.Replace("SELECT *", "SELECT TOP (100) *"),
            AlterarRequiereSinDependencias = true,
            Literal = (tipo, valor) => LiteralParametro(new ParametroModelo { Nombre = "v", TipoDatos = tipo }, new Dictionary<string, string?> { ["v"] = valor }),
            UnicoUnSoloNulo = true,
            Texto = (tipo, valor) => $"{(tipo.StartsWith('n') ? "N" : "")}'{valor.Replace("'", "''")}'",
            SoloLectura = tipo => tipo.Split('(')[0] is "timestamp" or "rowversion" or "image" or "varbinary" or "binary" or "geography" or "geometry" or "hierarchyid" or "sql_variant"
        };

        public ScriptModificacionModelo ScriptModificarTabla(PlanModificacionModelo plan)
        {
            var original = plan.Original;
            var tabla = NombreCompleto(original.Tabla);
            var nombreFinal = NombreFinal(plan);
            var noAplicables = new List<string>(plan.MotivosRecreacion);
            var sentencias = new List<SentenciaCambioModelo>();
            var cambios = plan.Cambios.Where(c => c.Tipo != TipoCambioTabla.CambiarIdentidad).ToList();
            var columnasOriginales = original.Columnas.ToDictionary(c => c.Nombre, StringComparer.Ordinal);

            foreach (var identidad in plan.Cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarIdentidad))
            {
                noAplicables.Add($"{identidad.Frase}: SQL Server solo lo permite creando la tabla de nuevo y copiando {FilasTexto(original.Filas)}.");
            }

            void Agregar(string sql, string frase) => sentencias.Add(new SentenciaCambioModelo { Sql = sql, Frase = frase });

            foreach (var cambio in cambios.Where(c => c.Tipo is TipoCambioTabla.QuitarLlaveForanea or TipoCambioTabla.QuitarUnica or TipoCambioTabla.QuitarClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} DROP CONSTRAINT {Delimitar(cambio.Restriccion!)};", cambio.Frase);
            }

            var alterados = cambios.Where(c => c.Tipo is TipoCambioTabla.CambiarTipo or TipoCambioTabla.CambiarNulos)
                .GroupBy(c => c.Anterior!.Nombre).ToList();
            var predeterminadosQuitados = new Dictionary<string, (ColumnaModelo Columna, string Frase)>(StringComparer.Ordinal);

            foreach (var cambio in cambios)
            {
                var anterior = cambio.Anterior is null ? null : columnasOriginales.GetValueOrDefault(cambio.Anterior.Nombre);

                if (anterior?.NombrePredeterminado is null || predeterminadosQuitados.ContainsKey(anterior.Nombre))
                {
                    continue;
                }

                if (cambio.Tipo is TipoCambioTabla.QuitarColumna or TipoCambioTabla.CambiarPredeterminado
                    || alterados.Any(g => g.Key == anterior.Nombre))
                {
                    predeterminadosQuitados[anterior.Nombre] = (anterior, cambio.Frase);
                    Agregar($"ALTER TABLE {tabla} DROP CONSTRAINT {Delimitar(anterior.NombrePredeterminado)};", cambio.Frase);
                }
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarColumna))
            {
                Agregar($"ALTER TABLE {tabla} DROP COLUMN {Delimitar(cambio.Anterior!.Nombre)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarColumna))
            {
                Agregar($"EXEC sp_rename N'{Literal($"{tabla}.{Delimitar(cambio.Anterior!.Nombre)}")}', N'{Literal(cambio.Nueva!.Nombre.Trim())}', N'COLUMN';", cambio.Frase);
            }

            foreach (var grupo in alterados)
            {
                var nueva = grupo.First().Nueva!;
                Agregar($"ALTER TABLE {tabla} ALTER COLUMN {Delimitar(nueva.Nombre.Trim())} {nueva.TipoCompleto} {(nueva.PermiteNulos ? "NULL" : "NOT NULL")};",
                    string.Join(". ", grupo.Select(c => c.Frase)));
            }

            foreach (var (nombre, (columna, frase)) in predeterminadosQuitados)
            {
                var nueva = plan.Nueva.Columnas.FirstOrDefault(c => c.NombreOriginal == nombre);
                var cambioPredeterminado = cambios.FirstOrDefault(c => c.Tipo == TipoCambioTabla.CambiarPredeterminado && c.Anterior!.Nombre == nombre);

                if (nueva is null || cambioPredeterminado is not null)
                {
                    continue;
                }

                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Delimitar(columna.NombrePredeterminado!)} DEFAULT ({NarradorTablaService.QuitarParentesisExternos(columna.ValorPredeterminado!)}) FOR {Delimitar(nueva.Nombre.Trim())};", frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarPredeterminado))
            {
                if (ComparadorTablaService.Predeterminado(cambio.Nueva!.ValorPredeterminado) is { } valor)
                {
                    var nombreRestriccion = columnasOriginales.GetValueOrDefault(cambio.Anterior!.Nombre)?.NombrePredeterminado
                        ?? $"DF_{nombreFinal}_{cambio.Nueva.Nombre.Trim()}";
                    Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Delimitar(nombreRestriccion)} DEFAULT ({valor}) FOR {Delimitar(cambio.Nueva.Nombre.Trim())};", cambio.Frase);
                }
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarColumna))
            {
                Agregar($"ALTER TABLE {tabla} ADD {DefinicionColumna(cambio.Nueva!, nombreFinal)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CrearClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Delimitar($"PK_{nombreFinal}")} PRIMARY KEY ({string.Join(", ", cambio.Columnas.Select(Delimitar))});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarUnica))
            {
                var columna = cambio.Nueva!.Nombre.Trim();
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Delimitar($"UQ_{nombreFinal}_{columna}")} UNIQUE ({Delimitar(columna)});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarLlaveForanea))
            {
                Agregar($"ALTER TABLE {tabla} WITH CHECK ADD {RestriccionLlave(nombreFinal, cambio.Nueva!.Nombre.Trim(), cambio.Nueva.Referencia!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarTabla))
            {
                Agregar($"EXEC sp_rename N'{Literal(tabla)}', N'{Literal(nombreFinal)}';", cambio.Frase);
            }

            var texto = new StringBuilder();

            if (sentencias.Count > 0)
            {
                texto.AppendLine("SET XACT_ABORT ON;");
                texto.AppendLine("BEGIN TRANSACTION;");
                texto.AppendLine();

                foreach (var sentencia in sentencias)
                {
                    texto.AppendLine(sentencia.Sql);
                }

                texto.AppendLine();
                texto.Append("COMMIT TRANSACTION;");
            }

            return new ScriptModificacionModelo { Sentencias = sentencias, NoAplicables = noAplicables, Texto = texto.ToString() };
        }

        public async Task<ResultadoModificacionModelo> AplicarModificacionAsync(
            ConexionActiva conexion,
            string baseDatos,
            ScriptModificacionModelo script,
            CancellationToken cancelacion = default)
        {
            var resultado = await EjecutarAsync(conexion, baseDatos, script.Texto, 1, cancelacion);

            return resultado.Errores.FirstOrDefault() is { } error
                ? new ResultadoModificacionModelo { Error = error.Texto }
                : new ResultadoModificacionModelo { Aplicados = script.Sentencias.Select(s => s.Frase).Distinct().ToList() };
        }

        public async Task<string> ScriptRecrearTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            PlanModificacionModelo plan,
            CancellationToken cancelacion = default)
        {
            var original = plan.Original;
            var esquema = original.Tabla.Esquema;
            var tabla = NombreCompleto(original.Tabla);
            var nombreFinal = NombreFinal(plan);
            var tablaFinal = $"{Delimitar(esquema)}.{Delimitar(nombreFinal)}";
            var temporal = $"{Delimitar(esquema)}.{Delimitar($"Tmp_{nombreFinal}")}";
            var nuevas = plan.Nueva.Columnas;
            var renombres = ComparadorTablaService.Renombres(plan, n => n);
            var columnasOriginales = original.Columnas.ToDictionary(c => c.Nombre, StringComparer.Ordinal);
            var s = new StringBuilder();

            s.AppendLine($"-- Crea de nuevo la tabla {original.Tabla.NombreCompleto} con la estructura nueva y copia sus {original.Filas:N0} filas.");
            s.AppendLine("-- Revísalo antes de ejecutarlo con F5. Va en una transacción: si algo falla, la tabla queda como estaba.");
            s.AppendLine("-- Las vistas y procedimientos que usan la tabla no se tocan; si usan columnas que cambiaron, revísalos después.");
            s.AppendLine($"USE {Delimitar(baseDatos)};");
            s.AppendLine("GO");
            s.AppendLine();
            s.AppendLine("SET XACT_ABORT ON;");
            s.AppendLine("BEGIN TRANSACTION;");
            s.AppendLine();

            var referencias = original.Referencias.Where(r => r.TablaOrigen.NombreCompleto != original.Tabla.NombreCompleto).ToList();

            if (referencias.Count > 0)
            {
                s.AppendLine("-- 1. Quitar las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine($"ALTER TABLE {NombreCompleto(referencia.TablaOrigen)} DROP CONSTRAINT {Delimitar(referencia.Nombre)};");
                }

                s.AppendLine();
            }

            s.AppendLine("-- 2. Crear la tabla nueva con un nombre temporal");
            s.AppendLine($"CREATE TABLE {temporal} (");
            s.AppendLine(string.Join($",{Environment.NewLine}", nuevas.Select(c => "    " + (c.EsCalculada
                ? $"{Delimitar(c.Nombre.Trim())} AS ({NarradorTablaService.QuitarParentesisExternos(c.Formula!)}){(c.EsPersistente ? " PERSISTED" : "")}"
                : $"{Delimitar(c.Nombre.Trim())} {c.TipoCompleto}{(c.EsIdentidad ? $" IDENTITY({c.Semilla}, {c.Incremento})" : "")} {(c.PermiteNulos ? "NULL" : "NOT NULL")}"))));
            s.AppendLine(");");
            s.AppendLine();

            var copiadas = nuevas.Where(c => c.NombreOriginal is not null && !c.EsCalculada).ToList();
            var conIdentidad = copiadas.Any(c => c.EsIdentidad);

            s.AppendLine("-- 3. Copiar los datos");

            if (conIdentidad)
            {
                s.AppendLine($"SET IDENTITY_INSERT {temporal} ON;");
            }

            if (copiadas.Count > 0)
            {
                s.AppendLine($"INSERT INTO {temporal} ({string.Join(", ", copiadas.Select(c => Delimitar(c.Nombre.Trim())))})");
                s.AppendLine($"SELECT {string.Join(", ", copiadas.Select(c => Delimitar(c.NombreOriginal!)))}");
                s.AppendLine($"FROM {tabla} WITH (HOLDLOCK TABLOCKX);");
            }

            if (conIdentidad)
            {
                s.AppendLine($"SET IDENTITY_INSERT {temporal} OFF;");
            }

            s.AppendLine();
            s.AppendLine("-- 4. Borrar la tabla original y darle su nombre a la nueva");
            s.AppendLine($"DROP TABLE {tabla};");
            s.AppendLine($"EXEC sp_rename N'{Literal(temporal)}', N'{Literal(nombreFinal)}';");
            s.AppendLine();
            s.AppendLine("-- 5. Volver a crear la clave principal, los valores únicos, los predeterminados, las restricciones CHECK y los índices");

            var clave = nuevas.Where(c => c.EsClavePrincipal).Select(c => Delimitar(c.Nombre.Trim())).ToList();

            if (clave.Count > 0)
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Delimitar(original.ClavePrincipal?.Nombre ?? $"PK_{nombreFinal}")} {(original.ClavePrincipal?.EsAgrupado == false ? "PRIMARY KEY NONCLUSTERED" : "PRIMARY KEY")} ({string.Join(", ", clave)});");
            }

            var unicasOriginales = ComparadorTablaService.ColumnasUnicas(original);

            foreach (var columna in nuevas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                var nombre = columna.NombreOriginal is not null && unicasOriginales.TryGetValue(columna.NombreOriginal, out var existente)
                    ? existente
                    : $"UQ_{nombreFinal}_{columna.Nombre.Trim()}";
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Delimitar(nombre)} UNIQUE ({Delimitar(columna.Nombre.Trim())});");
            }

            foreach (var indice in original.Indices.Where(i => i.EsRestriccionUnica && i.ColumnasClave.Count() > 1))
            {
                if (ComparadorTablaService.Mapear(indice.ColumnasClave, renombres) is { } columnas)
                {
                    s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Delimitar(indice.Nombre)} UNIQUE ({string.Join(", ", columnas.Select(Delimitar))});");
                }
            }

            foreach (var columna in nuevas.Where(c => !c.EsCalculada && ComparadorTablaService.Predeterminado(c.ValorPredeterminado) is not null))
            {
                var nombre = columna.NombreOriginal is not null && columnasOriginales.GetValueOrDefault(columna.NombreOriginal)?.NombrePredeterminado is { } existente
                    ? existente
                    : $"DF_{nombreFinal}_{columna.Nombre.Trim()}";
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Delimitar(nombre)} DEFAULT ({ComparadorTablaService.Predeterminado(columna.ValorPredeterminado)}) FOR {Delimitar(columna.Nombre.Trim())};");
            }

            foreach (var check in original.Restricciones)
            {
                s.AppendLine(check.Columna is not null && !renombres.ContainsKey(check.Columna)
                    ? $"-- La restricción CHECK {check.Nombre} usaba la columna quitada {check.Columna}; no se vuelve a crear."
                    : $"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Delimitar(check.Nombre)} CHECK ({RenombrarEnTexto(NarradorTablaService.QuitarParentesisExternos(check.Definicion), renombres)});");
            }

            foreach (var indice in original.Indices.Where(i => !i.EsClavePrincipal && !i.EsRestriccionUnica))
            {
                var claves = ComparadorTablaService.Mapear(indice.Columnas.Where(c => !c.Incluida).Select(c => c.Nombre), renombres);
                var incluidas = ComparadorTablaService.Mapear(indice.Columnas.Where(c => c.Incluida).Select(c => c.Nombre), renombres);

                if (claves is null || incluidas is null)
                {
                    s.AppendLine($"-- El índice {indice.Nombre} usaba una columna quitada; no se vuelve a crear.");
                    continue;
                }

                var descendentes = indice.Columnas.Where(c => !c.Incluida).Select(c => c.Descendente).ToList();
                var lista = string.Join(", ", claves.Select((c, i) => $"{Delimitar(c)}{(descendentes[i] ? " DESC" : "")}"));
                s.AppendLine($"CREATE {(indice.EsUnico ? "UNIQUE " : "")}{(indice.EsAgrupado ? "CLUSTERED" : "NONCLUSTERED")} INDEX {Delimitar(indice.Nombre)} ON {tablaFinal} ({lista}){(incluidas.Count > 0 ? $" INCLUDE ({string.Join(", ", incluidas.Select(Delimitar))})" : "")};");
            }

            s.AppendLine();
            s.AppendLine("-- 6. Llaves foráneas de esta tabla");

            foreach (var columna in nuevas.Where(c => c.Referencia is { ColumnaDestino.Length: > 0 }))
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} WITH CHECK ADD {RestriccionLlave(nombreFinal, columna.Nombre.Trim(), columna.Referencia!)};");
            }

            foreach (var llave in original.LlavesForaneas.Where(l => l.ColumnasOrigen.Count > 1))
            {
                if (ComparadorTablaService.Mapear(llave.ColumnasOrigen, renombres) is { } columnas)
                {
                    s.AppendLine($"ALTER TABLE {tablaFinal} WITH CHECK ADD CONSTRAINT {Delimitar(llave.Nombre)} FOREIGN KEY ({string.Join(", ", columnas.Select(Delimitar))}) REFERENCES {NombreCompleto(llave.TablaDestino)} ({string.Join(", ", llave.ColumnasDestino.Select(Delimitar))}){Reglas(llave)};");
                }
            }

            if (referencias.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 7. Volver a crear las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine(ComparadorTablaService.Mapear(referencia.ColumnasDestino, renombres) is { } destino
                        ? $"ALTER TABLE {NombreCompleto(referencia.TablaOrigen)} WITH CHECK ADD CONSTRAINT {Delimitar(referencia.Nombre)} FOREIGN KEY ({string.Join(", ", referencia.ColumnasOrigen.Select(Delimitar))}) REFERENCES {tablaFinal} ({string.Join(", ", destino.Select(Delimitar))}){Reglas(referencia)};"
                        : $"-- La llave foránea {referencia.Nombre} de {referencia.TablaOrigen.NombreCompleto} apuntaba a una columna quitada; no se vuelve a crear.");
                }
            }

            var disparadores = await DefinicionesDisparadoresAsync(conexion, baseDatos, original.Tabla, cancelacion);

            if (disparadores.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 8. Volver a crear los disparadores de la tabla");

                foreach (var definicion in disparadores)
                {
                    s.AppendLine($"EXEC sp_executesql N'{Literal(_tablaDelDisparador.Replace(definicion.Trim(), $"${{antes}}{tablaFinal}", 1))}';");
                }
            }

            s.AppendLine();
            s.AppendLine("COMMIT TRANSACTION;");
            s.AppendLine("GO");
            return s.ToString();
        }

        private static async Task<List<string>> DefinicionesDisparadoresAsync(ConexionActiva conexion, string baseDatos, TablaModelo tabla, CancellationToken cancelacion)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand("""
                SELECT OBJECT_DEFINITION(object_id)
                FROM sys.triggers
                WHERE parent_id = OBJECT_ID(@nombre) AND OBJECT_DEFINITION(object_id) IS NOT NULL
                ORDER BY name
                """, conexionSql);
            comando.Parameters.AddWithValue("@nombre", NombreCompleto(tabla));

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var definiciones = new List<string>();

            while (await lector.ReadAsync(cancelacion))
            {
                definiciones.Add(lector.GetString(0));
            }

            return definiciones;
        }

        private static string DefinicionColumna(DefinicionColumnaModelo columna, string nombreTabla)
        {
            var nombre = columna.Nombre.Trim();
            var texto = new StringBuilder($"{Delimitar(nombre)} {columna.TipoCompleto}");

            if (columna.EsIdentidad)
            {
                texto.Append($" IDENTITY({columna.Semilla}, {columna.Incremento})");
            }

            texto.Append(columna.PermiteNulos ? " NULL" : " NOT NULL");

            if (ComparadorTablaService.Predeterminado(columna.ValorPredeterminado) is { } valor)
            {
                texto.Append($" CONSTRAINT {Delimitar($"DF_{nombreTabla}_{nombre}")} DEFAULT ({valor})");
            }

            return texto.ToString();
        }

        private static string Reglas(LlaveForaneaModelo llave) =>
            (llave.AlEliminar == ReglaReferencial.SinAccion ? "" : $" ON DELETE {llave.AlEliminar.Sql()}")
            + (llave.AlActualizar == ReglaReferencial.SinAccion ? "" : $" ON UPDATE {llave.AlActualizar.Sql()}");

        private static string NombreFinal(PlanModificacionModelo plan) =>
            string.IsNullOrWhiteSpace(plan.Nueva.Nombre) ? plan.Original.Tabla.Nombre : plan.Nueva.Nombre.Trim();

        private static string RenombrarEnTexto(string texto, Dictionary<string, string> renombres)
        {
            foreach (var (anterior, nueva) in renombres.Where(r => r.Key != r.Value))
            {
                texto = texto.Replace(Delimitar(anterior), Delimitar(nueva));
            }

            return texto;
        }

        private static string Literal(string texto) => texto.Replace("'", "''");

        private static string FilasTexto(long filas) => filas == 1 ? "su única fila" : $"sus {filas:N0} filas";
    }
}
