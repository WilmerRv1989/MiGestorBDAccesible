using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Descripcion
{
    public class NarradorTablaService
    {
        public DescripcionTablaModelo Describir(DetalleTablaModelo detalle, TerminosMotorModelo? terminos = null)
        {
            terminos ??= CapacidadesMotorModelo.SqlServer.Terminos;
            var tabla = detalle.Tabla.NombreCorto();

            var resumen = new List<string>
            {
                $"{tabla} tiene {Contar(detalle.Columnas.Count, "columna", "columnas")} y {Contar(detalle.Filas, "fila", "filas")}."
            };

            if (detalle.Almacenamiento is { } almacenamiento && !almacenamiento.Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
            {
                resumen.Add($"Se guarda con el motor {almacenamiento} (ENGINE), que no admite llaves foráneas ni transacciones.");
            }

            if (detalle.EsTablaIntermedia)
            {
                var destinos = detalle.LlavesForaneas.Select(l => l.TablaDestino.NombreCorto()).Distinct().ToList();
                resumen.Add($"Es una tabla intermedia: relaciona {Unir(destinos)} en una relación de muchos a muchos.");
            }

            var secciones = new List<SeccionDescripcionModelo>
            {
                new() { Titulo = "Columnas", Frases = detalle.Columnas.Select(c => DescribirColumna(c, detalle, terminos)).ToList() },
                new() { Titulo = "Clave principal", Frases = [DescribirClavePrincipal(detalle)] }
            };

            var relaciones = detalle.LlavesForaneas.Select(l => DescribirLlave(l, detalle, terminos)).ToList();

            secciones.Add(new()
            {
                Titulo = "A qué tablas apunta",
                Frases = relaciones.Count > 0 ? relaciones : [$"{tabla} no apunta a ninguna otra tabla."]
            });

            secciones.Add(new()
            {
                Titulo = "Qué tablas apuntan a esta",
                Frases = detalle.Referencias.Count > 0
                    ? detalle.Referencias.Select(r => DescribirReferencia(r, detalle, terminos)).ToList()
                    : [$"Ninguna tabla apunta a {tabla}."]
            });

            if (detalle.RelacionesMuchosAMuchos.Count > 0)
            {
                secciones.Add(new()
                {
                    Titulo = "Relaciones de muchos a muchos",
                    Frases = detalle.RelacionesMuchosAMuchos.Select(r =>
                        $"A través de {r.TablaIntermedia.NombreCorto()}, {tabla} se relaciona con {r.OtraTabla.NombreCorto()}: una fila de {tabla} puede estar ligada a muchas filas de {r.OtraTabla.NombreCorto()}, y al revés.").ToList()
                });
            }

            secciones.Add(new()
            {
                Titulo = "Índices",
                Frases = detalle.Indices.Count > 0
                    ? detalle.Indices.Select(i => DescribirIndice(i, terminos)).ToList()
                    : [$"{tabla} no tiene índices."]
            });

            if (detalle.Restricciones.Count > 0)
            {
                secciones.Add(new()
                {
                    Titulo = "Restricciones CHECK",
                    Frases = detalle.Restricciones.Select(r => $"{r.Nombre}: {QuitarParentesisExternos(r.Definicion)}.").ToList()
                });
            }

            return new DescripcionTablaModelo
            {
                Titulo = $"Descripción de {detalle.Tabla.NombreCompleto}",
                Resumen = resumen,
                Secciones = secciones
            };
        }

        public List<string> DescribirDefinicion(DefinicionTablaModelo definicion, TerminosMotorModelo? terminos = null)
        {
            var tabla = new TablaModelo
            {
                Esquema = string.IsNullOrWhiteSpace(definicion.Esquema) ? "dbo" : definicion.Esquema.Trim(),
                EsquemaPredeterminado = string.IsNullOrWhiteSpace(definicion.Esquema) ? "dbo" : definicion.Esquema.Trim(),
                Nombre = string.IsNullOrWhiteSpace(definicion.Nombre) ? "(sin nombre)" : definicion.Nombre.Trim()
            };

            var columnas = definicion.Columnas.Select(c => new ColumnaModelo
            {
                Nombre = string.IsNullOrWhiteSpace(c.Nombre) ? "(sin nombre)" : c.Nombre.Trim(),
                TipoDatos = c.TipoCompleto,
                PermiteNulos = c.PermiteNulos,
                EsIdentidad = c.EsIdentidad,
                SemillaIdentidad = c.Semilla,
                IncrementoIdentidad = c.Incremento,
                ValorPredeterminado = string.IsNullOrWhiteSpace(c.ValorPredeterminado) ? null : c.ValorPredeterminado.Trim()
            }).ToList();

            var indices = new List<IndiceModelo>();
            var clave = definicion.Columnas.Where(c => c.EsClavePrincipal).Select(c => c.Nombre.Trim()).ToList();

            if (clave.Count > 0)
            {
                indices.Add(new IndiceModelo
                {
                    Nombre = $"PK_{tabla.Nombre}",
                    EsClavePrincipal = true,
                    EsUnico = true,
                    Columnas = clave.Select(n => new ColumnaIndiceModelo { Nombre = n }).ToList()
                });
            }

            indices.AddRange(definicion.Columnas.Where(c => c.EsUnica).Select(c => new IndiceModelo
            {
                Nombre = $"UQ_{tabla.Nombre}_{c.Nombre.Trim()}",
                EsUnico = true,
                EsRestriccionUnica = true,
                Columnas = [new ColumnaIndiceModelo { Nombre = c.Nombre.Trim() }]
            }));

            var llaves = definicion.Columnas
                .Where(c => c.Referencia is { ColumnaDestino.Length: > 0 })
                .Select(c => new LlaveForaneaModelo
                {
                    Nombre = $"FK_{tabla.Nombre}_{c.Referencia!.TablaDestino.Nombre}_{c.Nombre.Trim()}",
                    TablaOrigen = tabla,
                    ColumnasOrigen = [c.Nombre.Trim()],
                    TablaDestino = c.Referencia.TablaDestino,
                    ColumnasDestino = [c.Referencia.ColumnaDestino],
                    AlEliminar = c.Referencia.AlEliminar,
                    AlActualizar = c.Referencia.AlActualizar
                })
                .ToList();

            var detalle = new DetalleTablaModelo { Tabla = tabla, Columnas = columnas, Indices = indices, LlavesForaneas = llaves };
            var frases = new List<string> { $"{tabla.NombreCorto()} tendrá {Contar(columnas.Count, "columna", "columnas")}." };

            if (detalle.EsTablaIntermedia)
            {
                frases.Add($"Será una tabla intermedia: relacionará {Unir(llaves.Select(l => l.TablaDestino.NombreCorto()).Distinct())} en una relación de muchos a muchos.");
            }

            frases.AddRange(columnas.Select(c => DescribirColumna(c, detalle, terminos)));

            if (columnas.Count > 0)
            {
                frases.Add(DescribirClavePrincipal(detalle));
            }

            frases.AddRange(llaves.Select(l => DescribirLlave(l, detalle, terminos ?? CapacidadesMotorModelo.SqlServer.Terminos)));

            return frases;
        }

        public string DescribirColumna(ColumnaModelo columna, DetalleTablaModelo detalle, TerminosMotorModelo? terminos = null)
        {
            terminos ??= CapacidadesMotorModelo.SqlServer.Terminos;
            var partes = new List<string> { columna.TipoDatos };
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];

            if (clave.Contains(columna.Nombre, StringComparer.OrdinalIgnoreCase))
            {
                partes.Add(clave.Count == 1 ? "clave principal (PRIMARY KEY)" : "parte de la clave principal compuesta (PRIMARY KEY)");
            }

            if (columna.EsIdentidad)
            {
                partes.Add(columna.SemillaIdentidad is null && columna.SiguienteIdentidad is { } siguiente
                    ? $"número automático que sube de {columna.IncrementoIdentidad} en {columna.IncrementoIdentidad} ({terminos.Identidad(null, columna.IncrementoIdentidad)}), el siguiente será {siguiente}"
                    : $"número automático que empieza en {columna.SemillaIdentidad} y sube de {columna.IncrementoIdentidad} en {columna.IncrementoIdentidad} ({terminos.Identidad(columna.SemillaIdentidad, columna.IncrementoIdentidad)})");

                if (columna.IdentidadSiempre is { } siempre)
                {
                    partes.Add(siempre
                        ? "no se puede escribir a mano (ALWAYS)"
                        : "también se puede escribir a mano (BY DEFAULT)");
                }
            }

            foreach (var llave in detalle.LlavesForaneas.Where(l => l.ColumnasOrigen.Contains(columna.Nombre, StringComparer.OrdinalIgnoreCase)))
            {
                var indice = llave.ColumnasOrigen.FindIndex(c => c.Equals(columna.Nombre, StringComparison.OrdinalIgnoreCase));
                partes.Add($"apunta a {llave.TablaDestino.NombreCorto()}.{llave.ColumnasDestino[indice]} (FOREIGN KEY)");
            }

            var esUnica = detalle.Indices.Any(i =>
                i.EsUnico && !i.EsClavePrincipal
                && i.ColumnasClave.Count() == 1
                && i.ColumnasClave.First().Equals(columna.Nombre, StringComparison.OrdinalIgnoreCase));

            if (esUnica)
            {
                partes.Add("valor único, no se puede repetir (UNIQUE)");
            }

            if (columna.EsCalculada)
            {
                partes.Add(columna.EsPersistente
                    ? $"calculada con la fórmula {columna.Formula} y guardada en disco ({terminos.ColumnaCalculadaGuardada})"
                    : $"calculada con la fórmula {columna.Formula} ({terminos.ColumnaCalculada})");
            }

            if (columna.ValorPredeterminado is not null)
            {
                partes.Add($"valor predeterminado {QuitarParentesisExternos(columna.ValorPredeterminado)} (DEFAULT)");
            }

            partes.Add(columna.PermiteNulos ? "opcional (NULL)" : "obligatoria (NOT NULL)");

            return $"{columna.Nombre}: {string.Join(", ", partes)}.";
        }

        private static string DescribirClavePrincipal(DetalleTablaModelo detalle)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList();

            return clave switch
            {
                null or { Count: 0 } => $"{detalle.Tabla.NombreCorto()} no tiene clave principal (PRIMARY KEY).",
                { Count: 1 } => $"La clave principal (PRIMARY KEY) es {clave[0]}.",
                _ => $"La clave principal (PRIMARY KEY) es compuesta: {Unir(clave)}. La combinación de esas columnas no se puede repetir."
            };
        }

        private static string DescribirLlave(LlaveForaneaModelo llave, DetalleTablaModelo detalle, TerminosMotorModelo terminos)
        {
            var origen = llave.TablaOrigen.NombreCorto();
            var destino = llave.TablaDestino.NombreCorto();
            var columnasOrigen = Unir(llave.ColumnasOrigen);
            var columnasDestino = Unir(llave.ColumnasDestino.Select(c => $"{destino}.{c}"));

            var cardinalidad = EsUnicaEn(detalle, llave.ColumnasOrigen)
                ? $"relación de uno a uno: cada fila de {origen} pertenece a una fila de {destino}, y una fila de {destino} tiene como máximo una fila en {origen}."
                : $"relación de uno a muchos: cada fila de {origen} pertenece a una fila de {destino}, y una fila de {destino} puede tener muchas filas en {origen}.";

            var frase = $"{columnasOrigen} apunta a {columnasDestino} (FOREIGN KEY), {cardinalidad}";

            var opcionales = detalle.Columnas
                .Where(c => c.PermiteNulos && llave.ColumnasOrigen.Contains(c.Nombre, StringComparer.OrdinalIgnoreCase))
                .Select(c => c.Nombre)
                .ToList();

            if (opcionales.Count > 0)
            {
                frase += $" La relación es opcional porque {Unir(opcionales)} admite NULL.";
            }

            frase += $" Al borrar en {destino} (ON DELETE): {TextoRegla(llave.AlEliminar, "borran")}.";

            if (terminos.TieneReglasAlActualizar)
            {
                frase += $" Al actualizar (ON UPDATE): {TextoRegla(llave.AlActualizar, "actualizan")}.";
            }

            if (!llave.Habilitada)
            {
                frase += $" {terminos.FraseLlaveDeshabilitada} ({terminos.LlaveDeshabilitada}).";
            }

            return frase;
        }

        private static string DescribirReferencia(LlaveForaneaModelo llave, DetalleTablaModelo detalle, TerminosMotorModelo terminos)
        {
            var origen = llave.TablaOrigen.NombreCorto();
            var tabla = detalle.Tabla.NombreCorto();
            var columnas = Unir(llave.ColumnasOrigen.Select(c => $"{origen}.{c}"));

            var frase = $"{columnas} apunta a esta tabla (FOREIGN KEY), relación de uno a muchos: una fila de {tabla} puede tener muchas filas en {origen}. Al borrar aquí (ON DELETE): {TextoRegla(llave.AlEliminar, "borran")}.";

            return llave.Habilitada ? frase : $"{frase} {terminos.FraseLlaveDeshabilitada} ({terminos.LlaveDeshabilitada}).";
        }

        public static string DescribirIndice(IndiceModelo indice, TerminosMotorModelo? terminos = null)
        {
            terminos ??= CapacidadesMotorModelo.SqlServer.Terminos;
            var tipo = indice.EsClavePrincipal ? "de la clave principal (PRIMARY KEY)"
                : indice.EsRestriccionUnica ? "de una restricción de valor único (UNIQUE)"
                : indice.EsUnico ? "único (UNIQUE INDEX)"
                : "normal";

            var columnas = Unir(indice.Columnas.Where(c => !c.Incluida)
                .Select(c => c.Descendente ? $"{c.Nombre} descendente (DESC)" : c.Nombre));

            var agrupacion = !terminos.TieneIndicesAgrupados ? ""
                : indice.EsAgrupado ? ", agrupado (CLUSTERED)" : ", no agrupado (NONCLUSTERED)";
            var metodo = indice.Metodo is { } nombreMetodo && nombreMetodo != "btree" ? $", de tipo {nombreMetodo}" : "";
            var frase = $"{indice.Nombre}: índice {tipo}{agrupacion}{metodo}, sobre {columnas}";

            var incluidas = indice.Columnas.Where(c => c.Incluida).Select(c => c.Nombre).ToList();

            if (incluidas.Count > 0)
            {
                frase += $", incluye {Unir(incluidas)} (INCLUDE)";
            }

            return frase + ".";
        }

        private static bool EsUnicaEn(DetalleTablaModelo detalle, List<string> columnas)
        {
            var conjunto = columnas.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return detalle.Indices.Any(i => i.EsUnico && conjunto.SetEquals(i.ColumnasClave));
        }

        private static string TextoRegla(ReglaReferencial regla, string verbo) => regla switch
        {
            ReglaReferencial.Cascada => $"en cascada, también se {verbo} las filas relacionadas (CASCADE)",
            ReglaReferencial.EstablecerNulo => "las filas relacionadas quedan en NULL (SET NULL)",
            ReglaReferencial.EstablecerPredeterminado => "las filas relacionadas toman su valor predeterminado (SET DEFAULT)",
            ReglaReferencial.Restringir => "restringido, no se permite si hay filas relacionadas y se comprueba al momento (RESTRICT)",
            _ => "sin acción, no se permite si hay filas relacionadas (NO ACTION)"
        };

        public static string QuitarParentesisExternos(string expresion)
        {
            var texto = expresion.Trim();

            while (texto.Length >= 2 && texto[0] == '(' && texto[^1] == ')' && CierraAlFinal(texto))
            {
                texto = texto[1..^1].Trim();
            }

            return texto;

            static bool CierraAlFinal(string texto)
            {
                var profundidad = 0;

                for (var i = 0; i < texto.Length; i++)
                {
                    profundidad += texto[i] switch { '(' => 1, ')' => -1, _ => 0 };

                    if (profundidad == 0 && i < texto.Length - 1)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static string Contar(long cantidad, string singular, string plural) =>
            cantidad == 1 ? $"1 {singular}" : $"{cantidad:N0} {plural}";

        public static string Unir(IEnumerable<string> elementos)
        {
            var lista = elementos.ToList();

            return lista.Count switch
            {
                0 => "",
                1 => lista[0],
                _ => $"{string.Join(", ", lista.Take(lista.Count - 1))} y {lista[^1]}"
            };
        }
    }
}
