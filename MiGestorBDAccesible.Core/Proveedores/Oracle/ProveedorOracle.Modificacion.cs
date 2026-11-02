using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        public DialectoSqlModelo Dialecto { get; } = new()
        {
            Columna = Identificador,
            Tabla = t => $"{Identificador(t.Esquema)}.{Identificador(t.Nombre)}",
            Longitud = c => $"LENGTH({c})",
            Primeras = consulta => $"{consulta} FETCH FIRST 100 ROWS ONLY",
            CambioDeTipoSoloVacia = (antes, despues) => FamiliaCambio(antes) == FamiliaCambio(despues),
            ReducirPrecisionSoloVacia = true,
            Literal = (tipo, valor) => LiteralParametro(new ParametroModelo { Nombre = "v", TipoDatos = Regex.Replace(tipo, @"\s*\(.*$", "").Trim() }, new Dictionary<string, string?> { ["v"] = valor }),
            NoVacio = c => $"TRIM({c}) IS NOT NULL",
            Fecha = (tipo, valor) => tipo.StartsWith("TIMESTAMP", StringComparison.Ordinal)
                ? $"TIMESTAMP '{valor}'"
                : valor.Length <= 10 ? $"DATE '{valor}'" : $"TO_DATE('{valor[..Math.Min(valor.Length, 19)]}', 'YYYY-MM-DD HH24:MI:SS')",
            SoloLectura = tipo => tipo is "BLOB" or "BFILE" or "LONG RAW" || tipo.StartsWith("RAW", StringComparison.Ordinal),
            InsertarPredeterminados = tabla => $"INSERT INTO {tabla} VALUES (DEFAULT);"
        };

        public ScriptModificacionModelo ScriptModificarTabla(PlanModificacionModelo plan)
        {
            var original = plan.Original;
            var tabla = $"{Identificador(original.Tabla.Esquema)}.{Identificador(original.Tabla.Nombre)}";
            var nombreFinal = ComparadorTablaService.NombreFinal(plan.Nueva.Nombre, original.Tabla.Nombre, NombreGuardado);
            var noAplicables = new List<string>(plan.MotivosRecreacion);
            var sentencias = new List<SentenciaCambioModelo>();
            var cambios = plan.Cambios;

            void Agregar(string sql, string frase) => sentencias.Add(new SentenciaCambioModelo { Sql = sql, Frase = frase });
            string Columna(DefinicionColumnaModelo columna) => Identificador(Final(columna));

            foreach (var cambio in cambios.Where(c => c.Tipo is TipoCambioTabla.QuitarLlaveForanea or TipoCambioTabla.QuitarUnica or TipoCambioTabla.QuitarClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} DROP CONSTRAINT {Identificador(cambio.Restriccion!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarColumna))
            {
                Agregar($"ALTER TABLE {tabla} DROP COLUMN {Identificador(cambio.Anterior!.Nombre)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarColumna))
            {
                Agregar($"ALTER TABLE {tabla} RENAME COLUMN {Identificador(cambio.Anterior!.Nombre)} TO {Columna(cambio.Nueva!)};", cambio.Frase);
            }

            foreach (var grupo in cambios.Where(c => c.Tipo is TipoCambioTabla.CambiarTipo or TipoCambioTabla.CambiarNulos).GroupBy(c => c.Anterior!.Nombre))
            {
                var nueva = grupo.First().Nueva!;
                var partes = new List<string>();

                if (grupo.Any(c => c.Tipo == TipoCambioTabla.CambiarTipo))
                {
                    partes.Add(nueva.TipoCompleto);
                }

                if (grupo.Any(c => c.Tipo == TipoCambioTabla.CambiarNulos))
                {
                    partes.Add(nueva.PermiteNulos ? "NULL" : "NOT NULL");
                }

                Agregar($"ALTER TABLE {tabla} MODIFY ({Columna(nueva)} {string.Join(" ", partes)});", string.Join(". ", grupo.Select(c => c.Frase)));
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarPredeterminado))
            {
                Agregar($"ALTER TABLE {tabla} MODIFY ({Columna(cambio.Nueva!)} DEFAULT {ComparadorTablaService.Predeterminado(cambio.Nueva!.ValorPredeterminado) ?? "NULL"});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarIdentidad))
            {
                var nueva = cambio.Nueva!;

                if (!cambio.Anterior!.EsIdentidad)
                {
                    noAplicables.Add($"{cambio.Frase}: Oracle solo lo permite creando la tabla de nuevo y copiando {(original.Filas == 1 ? "su única fila" : $"sus {original.Filas:N0} filas")}.");
                }
                else if (!nueva.EsIdentidad)
                {
                    Agregar($"ALTER TABLE {tabla} MODIFY ({Columna(nueva)} DROP IDENTITY);", cambio.Frase);
                }
                else
                {
                    Agregar($"ALTER TABLE {tabla} MODIFY ({Columna(nueva)} GENERATED ALWAYS AS IDENTITY (START WITH {nueva.Semilla} INCREMENT BY {nueva.Incremento}));", cambio.Frase);
                }
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarColumna))
            {
                Agregar($"ALTER TABLE {tabla} ADD ({DefinicionColumnaOracle(cambio.Nueva!, false)});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CrearClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Identificador($"PK_{nombreFinal}")} PRIMARY KEY ({string.Join(", ", cambio.Columnas.Select(Identificador))});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarUnica))
            {
                var columna = Final(cambio.Nueva!);
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Identificador($"UQ_{nombreFinal}_{columna}")} UNIQUE ({Identificador(columna)});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarLlaveForanea))
            {
                Agregar($"ALTER TABLE {tabla} ADD {RestriccionLlave(nombreFinal, Final(cambio.Nueva!), cambio.Nueva.Referencia!, original.Tabla.Esquema)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarTabla))
            {
                Agregar($"ALTER TABLE {tabla} RENAME TO {Identificador(nombreFinal)};", cambio.Frase);
            }

            return new ScriptModificacionModelo
            {
                Sentencias = sentencias,
                NoAplicables = noAplicables,
                Texto = string.Join(Environment.NewLine, sentencias.Select(s => s.Sql))
            };
        }

        public async Task<ResultadoModificacionModelo> AplicarModificacionAsync(
            ConexionActiva conexion,
            string baseDatos,
            ScriptModificacionModelo script,
            CancellationToken cancelacion = default)
        {
            var aplicados = new List<string>();

            foreach (var sentencia in script.Sentencias)
            {
                var resultado = await EjecutarAsync(conexion, baseDatos, sentencia.Sql, 1, cancelacion);

                if (resultado.Errores.FirstOrDefault() is { } error)
                {
                    return new ResultadoModificacionModelo
                    {
                        Aplicados = aplicados.Distinct().ToList(),
                        Error = error.Texto,
                        FraseFallida = sentencia.Frase
                    };
                }

                aplicados.Add(sentencia.Frase);
            }

            return new ResultadoModificacionModelo { Aplicados = aplicados.Distinct().ToList() };
        }

        public async Task<string> ScriptRecrearTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            PlanModificacionModelo plan,
            CancellationToken cancelacion = default)
        {
            var original = plan.Original;
            var esquema = Identificador(original.Tabla.Esquema);
            var tabla = $"{esquema}.{Identificador(original.Tabla.Nombre)}";
            var nombreFinal = ComparadorTablaService.NombreFinal(plan.Nueva.Nombre, original.Tabla.Nombre, NombreGuardado);
            var tablaFinal = $"{esquema}.{Identificador(nombreFinal)}";
            var temporal = $"{esquema}.{Identificador($"TMP_{nombreFinal}")}";
            var nuevas = plan.Nueva.Columnas;
            var renombres = ComparadorTablaService.Renombres(plan, NombreGuardado);
            var s = new StringBuilder();

            string Columna(DefinicionColumnaModelo c) => Identificador(Final(c));
            string Lista(IEnumerable<string> columnas) => string.Join(", ", columnas.Select(Identificador));

            s.AppendLine($"-- Crea de nuevo la tabla {original.Tabla.NombreCompleto} con la estructura nueva y copia sus {original.Filas:N0} filas.");
            s.AppendLine("-- Revísalo antes de ejecutarlo con F5. En Oracle cada cambio de estructura se confirma solo:");
            s.AppendLine("-- si algo falla a mitad, la tabla original queda en la papelera y se recupera con FLASHBACK TABLE … TO BEFORE DROP.");
            s.AppendLine();

            var referencias = original.Referencias.Where(r => r.TablaOrigen.NombreCompleto != original.Tabla.NombreCompleto).ToList();

            if (referencias.Count > 0)
            {
                s.AppendLine("-- 1. Quitar las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine($"ALTER TABLE {Identificador(referencia.TablaOrigen.Esquema)}.{Identificador(referencia.TablaOrigen.Nombre)} DROP CONSTRAINT {Identificador(referencia.Nombre)};");
                }

                s.AppendLine();
            }

            s.AppendLine("-- 2. Crear la tabla nueva con un nombre temporal");
            s.AppendLine($"CREATE TABLE {temporal} (");
            s.AppendLine(string.Join($",{Environment.NewLine}", nuevas.Select(c => "    " + DefinicionColumnaOracle(c, true))));
            s.AppendLine(");");
            s.AppendLine();

            var copiadas = nuevas.Where(c => c.NombreOriginal is not null && !c.EsCalculada).ToList();
            s.AppendLine("-- 3. Copiar los datos");

            if (copiadas.Count > 0)
            {
                s.AppendLine($"INSERT INTO {temporal} ({string.Join(", ", copiadas.Select(Columna))})");
                s.AppendLine($"SELECT {string.Join(", ", copiadas.Select(c => Identificador(c.NombreOriginal!)))}");
                s.AppendLine($"FROM {tabla};");
            }

            s.AppendLine("COMMIT;");
            s.AppendLine();
            s.AppendLine("-- 4. Borrar la tabla original (queda en la papelera) y darle su nombre a la nueva");
            s.AppendLine($"DROP TABLE {tabla};");
            s.AppendLine($"ALTER TABLE {temporal} RENAME TO {Identificador(nombreFinal)};");

            foreach (var columna in nuevas.Where(c => c.EsIdentidad))
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} MODIFY ({Columna(columna)} GENERATED ALWAYS AS IDENTITY (START WITH LIMIT VALUE));");
            }

            s.AppendLine();
            s.AppendLine("-- 5. Volver a crear la clave principal, los valores únicos, las restricciones CHECK y los índices");

            var clave = nuevas.Where(c => c.EsClavePrincipal).Select(c => Final(c)).ToList();

            if (clave.Count > 0)
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(original.ClavePrincipal?.Nombre ?? $"PK_{nombreFinal}")} PRIMARY KEY ({Lista(clave)});");
            }

            var unicas = ComparadorTablaService.ColumnasUnicas(original);

            foreach (var columna in nuevas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                var nombre = columna.NombreOriginal is not null && unicas.TryGetValue(columna.NombreOriginal, out var existente)
                    ? existente
                    : $"UQ_{nombreFinal}_{Final(columna)}";
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(nombre)} UNIQUE ({Columna(columna)});");
            }

            foreach (var indice in original.Indices.Where(i => i.EsRestriccionUnica && i.ColumnasClave.Count() > 1))
            {
                if (ComparadorTablaService.Mapear(indice.ColumnasClave, renombres) is { } columnas)
                {
                    s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(indice.Nombre)} UNIQUE ({Lista(columnas)});");
                }
            }

            foreach (var check in original.Restricciones)
            {
                s.AppendLine(check.Columna is not null && !renombres.ContainsKey(check.Columna)
                    ? $"-- La restricción CHECK {check.Nombre} usaba la columna quitada {check.Columna}; no se vuelve a crear."
                    : $"ALTER TABLE {tablaFinal} ADD {Restriccion(check.Nombre)}CHECK ({RenombrarPalabras(check.Definicion, renombres)});");
            }

            foreach (var indice in original.Indices.Where(i => !i.EsClavePrincipal && !i.EsRestriccionUnica))
            {
                var columnas = ComparadorTablaService.Mapear(indice.Columnas.Select(c => c.Nombre), renombres);

                s.AppendLine(columnas is null
                    ? $"-- El índice {indice.Nombre} usaba una columna quitada o una expresión; no se vuelve a crear."
                    : $"CREATE {(indice.EsUnico ? "UNIQUE " : "")}INDEX {Identificador(indice.Nombre)} ON {tablaFinal} ({string.Join(", ", columnas.Select((c, i) => $"{Identificador(c)}{(indice.Columnas[i].Descendente ? " DESC" : "")}"))});");
            }

            s.AppendLine();
            s.AppendLine("-- 6. Llaves foráneas de esta tabla");

            foreach (var columna in nuevas.Where(c => c.Referencia is { ColumnaDestino.Length: > 0 }))
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD {RestriccionLlave(nombreFinal, Final(columna), columna.Referencia!, original.Tabla.Esquema)};");
            }

            foreach (var llave in original.LlavesForaneas.Where(l => l.ColumnasOrigen.Count > 1))
            {
                if (ComparadorTablaService.Mapear(llave.ColumnasOrigen, renombres) is { } columnas)
                {
                    s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(llave.Nombre)} FOREIGN KEY ({Lista(columnas)}) REFERENCES {Identificador(llave.TablaDestino.Esquema)}.{Identificador(llave.TablaDestino.Nombre)} ({Lista(llave.ColumnasDestino)}){ReglaOracle(llave)};");
                }
            }

            if (referencias.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 7. Volver a crear las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    var origen = $"{Identificador(referencia.TablaOrigen.Esquema)}.{Identificador(referencia.TablaOrigen.Nombre)}";
                    s.AppendLine(ComparadorTablaService.Mapear(referencia.ColumnasDestino, renombres) is { } destino
                        ? $"ALTER TABLE {origen} ADD CONSTRAINT {Identificador(referencia.Nombre)} FOREIGN KEY ({Lista(referencia.ColumnasOrigen)}) REFERENCES {tablaFinal} ({Lista(destino)}){ReglaOracle(referencia)};"
                        : $"-- La llave foránea {referencia.Nombre} de {referencia.TablaOrigen.NombreCompleto} apuntaba a una columna quitada; no se vuelve a crear.");
                }
            }

            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            var disparadores = await LeerObjetoAsync(conexionOracle, """
                SELECT owner, trigger_name FROM all_triggers
                WHERE table_owner = :esquema AND table_name = :tabla AND base_object_type = 'TABLE'
                ORDER BY trigger_name
                """, new ObjetoBaseDatosModelo { Esquema = original.Tabla.Esquema, Nombre = original.Tabla.Nombre }, cancelacion,
                l => new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Esquema = l.GetString(0), Nombre = l.GetString(1) });

            if (disparadores.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 8. Volver a crear los disparadores de la tabla");

                foreach (var disparador in disparadores)
                {
                    var fuente = await LeerFuenteAsync(conexionOracle, disparador, "TRIGGER", cancelacion);
                    EscribirFuente(s, _tablaDelDisparadorOracle.Replace(fuente, $"${{antes}}{tablaFinal}", 1));
                }
            }

            return s.ToString();
        }

        private static readonly Regex _tablaDelDisparadorOracle = new(
            @"^(?<antes>.*?\sON\s+)(""[^""]+""|[\w$#]+)(\.(""[^""]+""|[\w$#]+))?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private string DefinicionColumnaOracle(DefinicionColumnaModelo columna, bool paraCopiar)
        {
            var texto = new StringBuilder($"{Identificador(Final(columna))} {columna.TipoCompleto}");

            if (columna.EsCalculada)
            {
                texto.Append($" GENERATED ALWAYS AS ({columna.Formula}) VIRTUAL");
                return texto.ToString();
            }

            if (columna.EsIdentidad)
            {
                texto.Append(paraCopiar ? " GENERATED BY DEFAULT AS IDENTITY" : " GENERATED ALWAYS AS IDENTITY");

                if (columna.Semilla != 1 || columna.Incremento != 1)
                {
                    texto.Append($" (START WITH {columna.Semilla} INCREMENT BY {columna.Incremento})");
                }

                return texto.ToString();
            }

            if (ComparadorTablaService.Predeterminado(columna.ValorPredeterminado) is { } valor)
            {
                texto.Append($" DEFAULT {valor}");
            }

            if (!columna.PermiteNulos)
            {
                texto.Append(" NOT NULL");
            }

            return texto.ToString();
        }

        private string Final(DefinicionColumnaModelo columna) =>
            ComparadorTablaService.NombreFinal(columna.Nombre, columna.NombreOriginal, NombreGuardado);

        private static string ReglaOracle(LlaveForaneaModelo llave) =>
            llave.AlEliminar is ReglaReferencial.Cascada or ReglaReferencial.EstablecerNulo ? $" ON DELETE {llave.AlEliminar.Sql()}" : "";

        private static string FamiliaCambio(string tipo) => Regex.Replace(tipo, @"\s*\(.*$", "").Trim().ToUpperInvariant() switch
        {
            "VARCHAR2" or "CHAR" or "NVARCHAR2" or "NCHAR" => "TEXTO",
            "NUMBER" or "INTEGER" or "FLOAT" => "NUMERO",
            var t when t == "DATE" || t.StartsWith("TIMESTAMP", StringComparison.Ordinal) => "FECHA",
            var t => t
        };

        public static string RenombrarPalabras(string texto, Dictionary<string, string> renombres)
        {
            foreach (var (anterior, nueva) in renombres.Where(r => r.Key != r.Value))
            {
                texto = Regex.Replace(texto, $@"(?<![\w$#""]){Regex.Escape(anterior)}(?![\w$#""])|""{Regex.Escape(anterior)}""", Identificador(nueva), RegexOptions.IgnoreCase);
            }

            return texto;
        }
    }
}
